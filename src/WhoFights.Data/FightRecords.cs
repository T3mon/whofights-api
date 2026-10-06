using System.Text.RegularExpressions;

namespace WhoFights.Data;

// The one shape a fight record takes anywhere we show it: "W-L" or "W-L-D".
// Our two sources disagree on the details - Tapology writes "25-7-1",
// Wikipedia "24-9-1 (1 NC)" and sometimes en dashes - so every record goes
// through here before it reaches the site.
public static partial class FightRecords
{
    /// <summary>
    /// "24–9–1 (1 NC)" -> "24-9-1". Anything that doesn't come out as a plain
    /// W-L or W-L-D is rejected (null) rather than shown - Wikipedia is open
    /// to anyone's edits, and a record like "lol" must never reach the page.
    /// </summary>
    public static string? Normalize(string? record)
    {
        if (string.IsNullOrWhiteSpace(record)) return null;

        var cleaned = NoContests().Replace(record, "")
            .Replace('\u2013', '-')   // en dash
            .Replace('\u2014', '-')   // em dash
            .Replace('\u2212', '-')   // minus sign
            .Trim();

        return Shape().IsMatch(cleaned) ? cleaned : null;
    }

    /// <summary>
    /// Which record to show for a ranked fighter: Tapology's whenever we have
    /// a valid one. The ranking source is a page anyone can edit, so a vandal
    /// can put any record there; Tapology is the one to trust. Ours is the
    /// record printed on the fighter's newest card - their record going into
    /// that fight - so after the fight it stays one result behind until their
    /// next card is announced. The ranking source only fills in for fighters
    /// we have no Tapology record for.
    /// </summary>
    public static string? Preferred(string? tapology, string? rankingSource) =>
        Normalize(tapology) ?? Normalize(rankingSource);

    [GeneratedRegex(@"\s*\(\s*\d+\s*NC\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NoContests();

    [GeneratedRegex(@"^\d{1,3}-\d{1,3}(-\d{1,3})?$")]
    private static partial Regex Shape();
}
