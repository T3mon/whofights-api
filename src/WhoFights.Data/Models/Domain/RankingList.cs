namespace WhoFights.Data.Models.Domain;

// One ranking table as a source publishes it - the UFC media rankings at
// middleweight, BoxRec's heavyweight top 10 - mirrored from the scraper's
// Firestore "rankings" collection. Each sync replaces it wholesale; the
// scraper keeps the history (rankingSnapshots), we only need the current
// state.
public class RankingList
{
    // The scraper's document id, e.g. "mma-ufc-middleweight" or
    // "boxing-boxrec-heavyweight". Stable across syncs, so it's the key.
    public required string Id { get; set; }

    public required string Sport { get; set; }      // "mma", "boxing"
    public required string List { get; set; }       // "ufc", "ufc-meta", "boxrec"
    public required string Division { get; set; }   // as the source names it, e.g. "Women's Bantamweight"

    // The source's own "rankings released on" date. Null when the source
    // doesn't state one (the boxing tables don't).
    public DateOnly? AsOf { get; set; }

    // Where the scraper read it: a Wikipedia page and the revision it parsed.
    public required string SourcePage { get; set; }
    public long SourceRevision { get; set; }

    public DateTimeOffset SyncedAt { get; set; }

    public ICollection<RankingEntry> Entries { get; set; } = new List<RankingEntry>();
}

public enum RankingPosition
{
    // Holds a belt - UFC's, or one of boxing's sanctioning bodies' (see Belt).
    Champion,
    // A numbered contender (see Rank).
    Ranked,
    // Whoever the source marks as top-rated in the division, mirrored as-is.
    TopRated,
}

public class RankingEntry
{
    public int Id { get; set; }

    public required string RankingListId { get; set; }
    public RankingList RankingList { get; set; } = null!;

    public RankingPosition Position { get; set; }

    // Ranked entries only. Ties repeat a number and the next one is skipped
    // (3, 3, 5) - the UFC's own rankings do this.
    public int? Rank { get; set; }

    // Champion entries only: "UFC", "WBA", "WBC", "IBF", "WBO".
    public string? Belt { get; set; }

    // The name exactly as the source spells it.
    public required string Name { get; set; }
    public string? WikiLink { get; set; }

    // Our fighter, when the name could be matched safely (see
    // FighterNameMatcher in WhoFights.Sync). Null for fighters we've never
    // seen on a synced card, and for names too ambiguous to guess.
    public long? FighterId { get; set; }
    public Fighter? Fighter { get; set; }
}
