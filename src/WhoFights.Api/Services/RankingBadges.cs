using Microsoft.EntityFrameworkCore;
using WhoFights.Api.Models.Api;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;

namespace WhoFights.Api.Services;

// Each fighter's standing, for the badges next to their name on event
// cards. Looked up by Tapology link - the link every bout already carries -
// through the fighter the ranking sync linked each ranked name to.
public class RankingBadges(ApplicationDbContext db)
{
    // The lists that badge a fighter, most preferred first. The UFC "Meta"
    // list is an alternative view of the same divisions, so cards stick to
    // the official one.
    private static readonly string[] Lists = ["ufc", "boxrec"];

    public async Task<Dictionary<string, RankingBadgeDto>> ForFightersAsync(IEnumerable<string> tapologyLinks, CancellationToken ct)
    {
        var links = tapologyLinks.Distinct().ToList();
        if (links.Count == 0) return [];

        var entries = await db.RankingEntries
            .Where(e => e.Fighter != null
                && links.Contains(e.Fighter.TapologyLink)
                && Lists.Contains(e.RankingList.List)
                && e.Position != RankingPosition.TopRated)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Fighter!.TapologyLink, e.RankingList.List, e.RankingList.Division, e.Position, e.Rank, e.Belt })
            .ToListAsync(ct);

        return entries
            .GroupBy(e => e.TapologyLink)
            .ToDictionary(g => g.Key, g =>
            {
                // Preferred list first; within it a title beats a contender
                // spot, so a champion who's also ranked elsewhere shows as
                // champion.
                var best = g
                    .OrderBy(e => Array.IndexOf(Lists, e.List))
                    .ThenBy(e => e.Position == RankingPosition.Champion ? 0 : 1)
                    .ThenBy(e => e.Rank)
                    .First();
                var belts = best.Position == RankingPosition.Champion
                    ? g.Where(e => e.List == best.List && e.Division == best.Division && e.Position == RankingPosition.Champion)
                        .Select(e => e.Belt ?? "")
                        .Where(b => b.Length > 0)
                        .ToList()
                    : [];
                return new RankingBadgeDto(best.List, best.Division, best.Position == RankingPosition.Ranked ? best.Rank : null, belts);
            });
    }

    // Every fighter link the given bouts mention.
    public static IEnumerable<string> LinksIn(IEnumerable<BoutDto> bouts) =>
        bouts.SelectMany(b => new[] { b.FighterALink, b.FighterBLink });

    public static BoutDto WithBadges(BoutDto bout, IReadOnlyDictionary<string, RankingBadgeDto> badges) => bout with
    {
        FighterARanking = badges.GetValueOrDefault(bout.FighterALink),
        FighterBRanking = badges.GetValueOrDefault(bout.FighterBLink),
    };
}
