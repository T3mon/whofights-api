using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WhoFights.Sync.Firestore;

// Reads the scraper's collections ("events", "rankings") straight from
// Firestore's public REST API. No credentials needed: the
// Tapology-firebase-scraper project's security rules allow anonymous reads
// of both (writes need auth, delete is blocked entirely) - see that repo's
// firestore.rules and README.
public class FirestoreClient(HttpClient httpClient, IOptions<FirestoreOptions> options, ILogger<FirestoreClient> logger)
{
    private readonly FirestoreOptions _options = options.Value;

    public Task<List<TapologyEventDto>> FetchAllEventsAsync(CancellationToken ct = default) =>
        FetchCollectionAsync(_options.CollectionName, ParseEvent, ct);

    public Task<List<RankingListDto>> FetchAllRankingsAsync(CancellationToken ct = default) =>
        FetchCollectionAsync(_options.RankingsCollectionName, ParseRankingList, ct);

    // Every document of a collection, page by page. A document that won't
    // parse is logged and skipped rather than failing the whole collection.
    private async Task<List<T>> FetchCollectionAsync<T>(string collection, Func<JsonElement, T> parse, CancellationToken ct)
    {
        var results = new List<T>();
        string? pageToken = null;

        do
        {
            var url = $"https://firestore.googleapis.com/v1/projects/{_options.ProjectId}/databases/(default)/documents/{collection}?pageSize=300"
                       + (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            using var response = await httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var docElement in documents.EnumerateArray())
                {
                    try
                    {
                        results.Add(parse(docElement));
                    }
                    catch (Exception ex)
                    {
                        var docName = docElement.TryGetProperty("name", out var n) ? n.GetString() : "(unknown)";
                        logger.LogWarning(ex, "Skipping malformed Firestore document {DocName}", docName);
                    }
                }
            }

            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
        } while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    private static TapologyEventDto ParseEvent(JsonElement docElement)
    {
        var name = docElement.GetProperty("name").GetString()!;
        var slug = name.Split('/').Last();
        var fields = docElement.GetProperty("fields");

        var organization = GetString(fields, "organization") ?? "Other";

        return new TapologyEventDto
        {
            Slug = slug,
            Title = GetString(fields, "title") ?? slug,
            Link = GetString(fields, "link") ?? "",
            Organization = organization,
            FullOrganization = GetString(fields, "fullOrganization") ?? organization,
            Date = GetString(fields, "date") ?? "",
            Venue = GetString(fields, "venue"),
            Location = GetString(fields, "location"),
            Fights = ParseFights(fields),
        };
    }

    private static List<TapologyFightDto> ParseFights(JsonElement fields)
    {
        var list = new List<TapologyFightDto>();

        if (!fields.TryGetProperty("fights", out var fightsField) ||
            !fightsField.TryGetProperty("arrayValue", out var arrayValue) ||
            !arrayValue.TryGetProperty("values", out var values))
        {
            return list;
        }

        foreach (var fightValue in values.EnumerateArray())
        {
            var fightFields = fightValue.GetProperty("mapValue").GetProperty("fields");

            var fighterA = ParseFighter(fightFields, "fighterA");
            var fighterB = ParseFighter(fightFields, "fighterB");
            if (fighterA is null || fighterB is null) continue;

            list.Add(new TapologyFightDto
            {
                FighterA = fighterA,
                FighterB = fighterB,
                WeightClass = GetString(fightFields, "weightClass"),
            });
        }

        return list;
    }

    private static TapologyFighterDto? ParseFighter(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var fighterValue) ||
            !fighterValue.TryGetProperty("mapValue", out var mapValue))
        {
            return null;
        }

        var fighterFields = mapValue.GetProperty("fields");
        var link = GetString(fighterFields, "link");
        var fighterName = GetString(fighterFields, "name");
        if (link is null || fighterName is null) return null;

        return new TapologyFighterDto
        {
            Name = fighterName,
            Record = GetString(fighterFields, "record"),
            Link = link,
        };
    }

    private static RankingListDto ParseRankingList(JsonElement docElement)
    {
        var id = docElement.GetProperty("name").GetString()!.Split('/').Last();
        var fields = docElement.GetProperty("fields");
        var source = GetMap(fields, "source");

        return new RankingListDto
        {
            Id = id,
            Sport = GetString(fields, "sport") ?? throw new FormatException($"Ranking {id} has no sport"),
            List = GetString(fields, "list") ?? throw new FormatException($"Ranking {id} has no list"),
            Division = GetString(fields, "division") ?? throw new FormatException($"Ranking {id} has no division"),
            AsOf = GetString(fields, "asOf"),
            SourcePage = source is { } s ? GetString(s, "page") ?? "" : "",
            SourceRevision = source is { } r ? GetLong(r, "revid") ?? 0 : 0,
            // A vacant belt comes through with a null fighter - nothing to show for it.
            Champions = GetArray(fields, "champions")
                .Select(c => ParseRankedFighter(GetMap(c, "fighter"), belt: GetString(c, "belt")))
                .OfType<RankedFighterDto>()
                .ToList(),
            Ranked = GetArray(fields, "ranked")
                .Select(r => ParseRankedFighter(r, rank: (int?)GetLong(r, "rank")))
                .OfType<RankedFighterDto>()
                .ToList(),
            // Mirrored from Wikipedia as-is: null, {fighter: null}, or a fighter.
            TopRated = GetMap(fields, "topRated") is { } top ? ParseRankedFighter(GetMap(top, "fighter")) : null,
        };
    }

    private static RankedFighterDto? ParseRankedFighter(JsonElement? fields, int? rank = null, string? belt = null) =>
        fields is { } f && GetString(f, "name") is { Length: > 0 } name
            ? new RankedFighterDto(name, GetString(f, "wikiLink"), GetString(f, "record"), rank, belt)
            : null;

    // Firestore's REST format wraps every value in a type tag, e.g.
    // {"stringValue": "foo"}, {"integerValue": "15"} (a string!) or
    // {"nullValue": null}. Anything of the wrong type, including nullValue,
    // reads as absent.
    private static string? GetString(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var value)) return null;
        return value.TryGetProperty("stringValue", out var str) ? str.GetString() : null;
    }

    private static long? GetLong(JsonElement fields, string key) =>
        fields.TryGetProperty(key, out var value)
        && value.TryGetProperty("integerValue", out var number)
        && long.TryParse(number.GetString(), out var parsed)
            ? parsed
            : null;

    // The "fields" of a map value; null when absent or null. An empty map
    // has no "fields" at all, which reads as a map with no keys.
    private static JsonElement? GetMap(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var value) || !value.TryGetProperty("mapValue", out var map)) return null;
        return map.TryGetProperty("fields", out var inner) ? inner : EmptyMap;
    }

    private static readonly JsonElement EmptyMap = JsonDocument.Parse("{}").RootElement;

    // The elements of an array value, each unwrapped to its map's "fields"
    // (every array in this feed holds maps). An empty array has no "values".
    private static List<JsonElement> GetArray(JsonElement fields, string key)
    {
        if (!fields.TryGetProperty(key, out var value)
            || !value.TryGetProperty("arrayValue", out var array)
            || !array.TryGetProperty("values", out var values))
        {
            return [];
        }

        return values.EnumerateArray()
            .Select(v => v.TryGetProperty("mapValue", out var m) && m.TryGetProperty("fields", out var f) ? f : (JsonElement?)null)
            .OfType<JsonElement>()
            .ToList();
    }
}
