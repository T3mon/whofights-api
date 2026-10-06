using Microsoft.EntityFrameworkCore;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;
using WhoFights.Sync.Firestore;

namespace WhoFights.Sync.Services;

public record RankingSyncResult(int Saved, int Rejected, int Removed, int Entries, int Linked);

// Mirrors the scraper's rankings into Postgres. Each list it publishes
// replaces ours wholesale; a list that fails the sanity checks keeps its
// previous version; and each name is linked to one of our fighters when the
// match is unambiguous (see FighterNameMatcher).
public class RankingSyncService(ApplicationDbContext db, ILogger<RankingSyncService> logger)
{
    // A list the scraper hasn't refreshed for this long is no longer
    // published (a division dropped from the source page) and goes. Long
    // enough that a few days of a broken source keep showing the last good
    // version instead.
    private static readonly TimeSpan RetireAfter = TimeSpan.FromDays(14);

    public async Task<RankingSyncResult> SyncAsync(IReadOnlyList<RankingListDto> lists, CancellationToken ct = default)
    {
        // An empty fetch means something upstream broke (rules, outage), not
        // that every ranking vanished overnight - keep what we have.
        if (lists.Count == 0)
        {
            logger.LogWarning("Ranking sync: Firestore returned no rankings, keeping the existing ones");
            return new RankingSyncResult(0, 0, 0, 0, 0);
        }

        var matcher = new FighterNameMatcher(await db.Fighters.AsNoTracking().ToListAsync(ct));
        var existing = await db.RankingLists.Include(l => l.Entries).ToDictionaryAsync(l => l.Id, ct);
        var now = DateTimeOffset.UtcNow;
        int saved = 0, rejected = 0, entries = 0, linked = 0;

        foreach (var dto in lists)
        {
            if (Problem(dto) is { } problem)
            {
                logger.LogWarning("Ranking sync: keeping the previous {List}, the new one is invalid: {Problem}", dto.Id, problem);
                rejected++;
                continue;
            }

            if (!existing.TryGetValue(dto.Id, out var list))
            {
                list = new RankingList { Id = dto.Id, Sport = dto.Sport, List = dto.List, Division = dto.Division, SourcePage = dto.SourcePage };
                db.RankingLists.Add(list);
            }
            else
            {
                db.RankingEntries.RemoveRange(list.Entries);
                list.Entries.Clear();
            }

            list.Sport = dto.Sport;
            list.List = dto.List;
            list.Division = dto.Division;
            list.AsOf = DateOnly.TryParseExact(dto.AsOf, "yyyy-MM-dd", out var asOf) ? asOf : null;
            list.SourcePage = dto.SourcePage;
            list.SourceRevision = dto.SourceRevision;
            list.SyncedAt = now;

            var incoming = dto.Champions.Select(f => (RankingPosition.Champion, f))
                .Concat(dto.Ranked.Select(f => (RankingPosition.Ranked, f)))
                .Concat(dto.TopRated is { } top ? [(RankingPosition.TopRated, top)] : []);
            foreach (var (position, fighter) in incoming)
            {
                var match = matcher.Match(fighter.Name);
                list.Entries.Add(new RankingEntry
                {
                    RankingListId = list.Id,
                    Position = position,
                    Rank = position == RankingPosition.Ranked ? fighter.Rank : null,
                    Belt = position == RankingPosition.Champion ? fighter.Belt : null,
                    Name = fighter.Name,
                    WikiLink = fighter.WikiLink,
                    Record = FightRecords.Normalize(fighter.Record),
                    FighterId = match?.Id,
                });
                entries++;
                if (match is not null) linked++;
            }

            saved++;
        }

        var retired = existing.Values.Where(l => now - l.SyncedAt > RetireAfter).ToList();
        db.RankingLists.RemoveRange(retired);

        await db.SaveChangesAsync(ct);
        return new RankingSyncResult(saved, rejected, retired.Count, entries, linked);
    }

    // The scraper validates too; this is the last line before the site shows
    // it. Ties are legitimate (3, 3, 5) - the UFC's own rankings have them.
    private static string? Problem(RankingListDto dto)
    {
        if (dto.Ranked.Count == 0 && dto.Champions.Count == 0) return "no fighters";

        for (var i = 0; i < dto.Ranked.Count; i++)
        {
            var rank = dto.Ranked[i].Rank;
            var previous = i == 0 ? null : dto.Ranked[i - 1].Rank;
            if (rank != i + 1 && (previous is null || rank != previous))
            {
                return $"rank {rank?.ToString() ?? "missing"} at position {i + 1}";
            }
        }

        var duplicate = dto.Ranked.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? null : $"{duplicate.Key} is ranked twice";
    }
}
