namespace WhoFights.Api.Models.Api;

/// <summary>One fighter in a ranking: a ranked contender, a belt holder, or the source's top-rated pick.</summary>
/// <param name="Name">The name as the ranking source spells it, which can differ from Tapology's spelling on event cards.</param>
/// <param name="Rank">1-based rank for contenders; ties repeat a number and skip the next (3, 3, 5). Null for champions and the top-rated entry.</param>
/// <param name="Belt">For champions: whose belt - "UFC", or a boxing sanctioning body ("WBA", "WBC", "IBF", "WBO").</param>
/// <param name="WikiLink">The fighter's Wikipedia article, when there is one.</param>
/// <param name="FighterLink">
/// The fighter's Tapology page, when the name could be linked to a fighter on one of our synced cards - the same
/// link event cards carry, so a card can look up a fighter's rank by it. Null when the fighter isn't on any synced
/// card or the name was too ambiguous to link safely.
/// </param>
public record RankingEntryDto(string Name, int? Rank, string? Belt, string? WikiLink, string? FighterLink);

/// <summary>A division's ranking as one source publishes it, mirrored daily from Wikipedia.</summary>
/// <param name="Id">Stable id, e.g. "mma-ufc-middleweight" or "boxing-boxrec-heavyweight".</param>
/// <param name="Sport">"mma" or "boxing".</param>
/// <param name="List">Which ranking: "ufc" (official UFC media rankings), "ufc-meta" (UFC's algorithmic Meta rankings), "boxrec".</param>
/// <param name="Division">Weight class as the source names it, e.g. "Women's Bantamweight".</param>
/// <param name="AsOf">The date the source says these rankings were released. Null when it doesn't say (boxing).</param>
/// <param name="SyncedAt">When this list was last refreshed from the source.</param>
/// <param name="Champions">Belt holders - one for UFC, up to four in boxing.</param>
/// <param name="Ranked">Contenders in rank order.</param>
/// <param name="TopRated">The source's top-rated fighter in the division, when it marks one (BoxRec).</param>
public record RankingDto(
    string Id,
    string Sport,
    string List,
    string Division,
    DateOnly? AsOf,
    DateTimeOffset SyncedAt,
    IReadOnlyList<RankingEntryDto> Champions,
    IReadOnlyList<RankingEntryDto> Ranked,
    RankingEntryDto? TopRated);
