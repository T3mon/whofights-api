namespace WhoFights.Sync.Firestore;

// One document of the scraper's "rankings" collection: a division's ranking
// as one source publishes it, read from Wikipedia by Tapology-firebase-scraper.
public class RankingListDto
{
    public required string Id { get; init; }
    public required string Sport { get; init; }
    public required string List { get; init; }
    public required string Division { get; init; }

    // "yyyy-MM-dd", or null when the source states no date (boxing).
    public string? AsOf { get; init; }

    public required string SourcePage { get; init; }
    public long SourceRevision { get; init; }

    public List<RankedFighterDto> Champions { get; init; } = [];
    public List<RankedFighterDto> Ranked { get; init; } = [];
    public RankedFighterDto? TopRated { get; init; }
}

/// <param name="Record">The record as the source lists it, e.g. "20-3 (1 NC)" - normalised before it's stored.</param>
/// <param name="Rank">Set for ranked entries only.</param>
/// <param name="Belt">Set for champions only ("UFC", "WBA", ...).</param>
public record RankedFighterDto(string Name, string? WikiLink, string? Record, int? Rank = null, string? Belt = null);
