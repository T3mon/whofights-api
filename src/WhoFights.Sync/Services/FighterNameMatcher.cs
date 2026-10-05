using System.Globalization;
using System.Text;
using WhoFights.Data.Models.Domain;

namespace WhoFights.Sync.Services;

// Links a name as a ranking source spells it ("Abusupiyan Magomedov") to one
// of our fighters, whose names come from Tapology ("Abus Magomedov"). Tries
// progressively looser comparisons and only accepts a match when exactly one
// fighter fits - pinning a rank on the wrong Magomedov is worse than showing
// none, so two fighters sharing a name stay unlinked.
public class FighterNameMatcher
{
    private readonly List<(Fighter Fighter, string[] Tokens)> _fighters;
    private readonly ILookup<string, Fighter> _byName;
    private readonly ILookup<string, Fighter> _byLetters;

    public FighterNameMatcher(IEnumerable<Fighter> fighters)
    {
        _fighters = fighters.Select(f => (f, Tokens(f.Name))).Where(x => x.Item2.Length > 0).ToList();
        _byName = _fighters.ToLookup(x => string.Join(' ', x.Tokens), x => x.Fighter);
        _byLetters = _fighters.ToLookup(x => string.Concat(x.Tokens), x => x.Fighter);
    }

    public Fighter? Match(string name)
    {
        var tokens = Tokens(name);
        if (tokens.Length == 0) return null;

        // 1. The same name once accents, case and punctuation are ignored ("Natalia Silva" = "Natália Silva").
        // 2. The same letters, spaced differently ("Su Mudaerji" = "Sumudaerji").
        // 3. The same surname, with a first name that's a short form of the other ("Abus" / "Abusupiyan").
        return Single(_byName[string.Join(' ', tokens)])
            ?? Single(_byLetters[string.Concat(tokens)])
            ?? Single(_fighters.Where(x => IsShortForm(x.Tokens, tokens)).Select(x => x.Fighter));
    }

    private static Fighter? Single(IEnumerable<Fighter> candidates)
    {
        var distinct = candidates.DistinctBy(f => f.Id).Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    // Same number of names and the same surname; every other name either
    // equal or one the start of the other, at least three letters long so a
    // bare initial can't match half the roster.
    private static bool IsShortForm(string[] a, string[] b) =>
        a.Length == b.Length && a.Length >= 2
        && a[^1] == b[^1]
        && a.Zip(b).SkipLast(1).All(p => p.First == p.Second
            || (Math.Min(p.First.Length, p.Second.Length) >= 3
                && (p.First.StartsWith(p.Second, StringComparison.Ordinal) || p.Second.StartsWith(p.First, StringComparison.Ordinal))));

    // "Lone'er Kavanagh" -> ["loneer", "kavanagh"], "Jean-Luc" -> ["jean", "luc"], "Á" -> "a".
    private static string[] Tokens(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (char.IsWhiteSpace(c) || c == '-') sb.Append(' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
