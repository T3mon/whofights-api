using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhoFights.Api.Models.Api;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;

namespace WhoFights.Api.Controllers.Api;

[ApiController]
[Route("api/rankings")]
public class RankingsController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>
    /// Current rankings per division. Mirrored daily from Wikipedia, which is CC BY-SA licensed - credit
    /// Wikipedia wherever these are shown.
    /// </summary>
    /// <param name="sport">"mma" or "boxing"; omit for every sport.</param>
    /// <param name="list">"ufc", "ufc-meta" or "boxrec"; omit for every list.</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RankingDto>>> GetRankings(
        [FromQuery] string? sport,
        [FromQuery] string? list,
        CancellationToken ct)
    {
        var query = db.RankingLists.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sport)) query = query.Where(l => l.Sport == sport);
        if (!string.IsNullOrWhiteSpace(list)) query = query.Where(l => l.List == list);

        var lists = await query
            .Include(l => l.Entries).ThenInclude(e => e.Fighter)
            .OrderBy(l => l.Sport).ThenBy(l => l.List).ThenBy(l => l.Division)
            .ToListAsync(ct);

        // Fighters with a card still ahead: their Tapology record is the one
        // going into that fight, i.e. current (see FightRecords.Choose).
        var now = DateTimeOffset.UtcNow;
        var upcoming = (await db.Bouts
                .Where(b => b.Event.StartsAt > now)
                .Select(b => new { b.FighterAId, b.FighterBId })
                .ToListAsync(ct))
            .SelectMany(b => new[] { b.FighterAId, b.FighterBId })
            .ToHashSet();

        return Ok(lists.Select(l => ToDto(l, upcoming)).ToList());
    }

    private static RankingDto ToDto(RankingList list, HashSet<long> fightersWithUpcomingCard)
    {
        // Entry ids follow the source's order, which keeps tied ranks in the
        // order the source lists them.
        var entries = list.Entries.OrderBy(e => e.Id).ToList();
        return new RankingDto(
            list.Id,
            list.Sport,
            list.List,
            list.Division,
            list.AsOf,
            list.SyncedAt,
            // The scraper reads Wikipedia; SourcePage is the article title it parsed.
            string.IsNullOrEmpty(list.SourcePage) ? null : $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(list.SourcePage)}",
            entries.Where(e => e.Position == RankingPosition.Champion).Select(Entry).ToList(),
            entries.Where(e => e.Position == RankingPosition.Ranked).OrderBy(e => e.Rank).ThenBy(e => e.Id).Select(Entry).ToList(),
            entries.Where(e => e.Position == RankingPosition.TopRated).Select(Entry).FirstOrDefault());

        RankingEntryDto Entry(RankingEntry entry) => new(
            entry.Name,
            entry.Rank,
            entry.Belt,
            FightRecords.Choose(
                entry.Fighter?.Record,
                entry.FighterId is { } id && fightersWithUpcomingCard.Contains(id),
                entry.Record),
            entry.WikiLink,
            entry.Fighter?.TapologyLink);
    }
}
