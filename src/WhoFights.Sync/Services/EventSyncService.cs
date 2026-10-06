using System.Globalization;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;
using WhoFights.Sync.Firestore;
using Microsoft.EntityFrameworkCore;

namespace WhoFights.Sync.Services;

// Transforms the raw Firestore feed into the normalized Postgres schema.
// Upserts by stable keys (Tapology's own slug/fighter ids) so re-running
// the sync refreshes existing rows instead of duplicating them.
public class EventSyncService(ApplicationDbContext db, ILogger<EventSyncService> logger)
{
    private static readonly TimeZoneInfo EasternTimeZone = ResolveEasternTimeZone();

    public async Task<EventSyncResult> SyncAsync(IReadOnlyList<TapologyEventDto> events, CancellationToken ct = default)
    {
        int created = 0, updated = 0, skipped = 0;

        var promotions = await db.Promotions.ToDictionaryAsync(p => p.Code, ct);
        var fighters = await db.Fighters.ToDictionaryAsync(f => f.Id, ct);
        var existingEvents = await db.Events.Include(e => e.Bouts).ToDictionaryAsync(e => e.TapologySlug, ct);

        // Oldest card first, so a fighter on several cards ends up with the
        // record from their newest one - each card lists the record as it
        // stood going into that fight.
        foreach (var dto in events.OrderBy(e => SortDate(e.Date)))
        {
            DateTimeOffset startsAt;
            try
            {
                startsAt = ParseTapologyDate(dto.Date);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping event {Slug}: unparseable date '{Date}'", dto.Slug, dto.Date);
                skipped++;
                continue;
            }

            if (!promotions.TryGetValue(dto.Organization, out var promotion))
            {
                promotion = new Promotion { Code = dto.Organization, Name = dto.FullOrganization };
                db.Promotions.Add(promotion);
                promotions[dto.Organization] = promotion;
            }

            if (!existingEvents.TryGetValue(dto.Slug, out var eventEntity))
            {
                eventEntity = new Event { TapologySlug = dto.Slug, Title = dto.Title, TapologyLink = dto.Link };
                db.Events.Add(eventEntity);
                existingEvents[dto.Slug] = eventEntity;
                created++;
            }
            else
            {
                // Simplest correct way to reflect card changes (added/removed/
                // reordered bouts) is to drop and rebuild them each sync,
                // rather than diffing - event counts are small enough that
                // this costs nothing.
                db.Bouts.RemoveRange(eventEntity.Bouts);
                eventEntity.Bouts.Clear();
                updated++;
            }

            eventEntity.Title = dto.Title;
            eventEntity.SubSeries = EventSeriesClassifier.ExtractSubSeries(dto.Organization, dto.Title);
            eventEntity.TapologyLink = dto.Link;
            eventEntity.Venue = dto.Venue;
            eventEntity.Location = dto.Location;
            eventEntity.StartsAt = startsAt;
            eventEntity.Promotion = promotion;

            var order = 0;
            foreach (var fight in dto.Fights)
            {
                var fighterA = GetOrCreateFighter(fighters, db, fight.FighterA);
                var fighterB = GetOrCreateFighter(fighters, db, fight.FighterB);
                if (fighterA is null || fighterB is null) continue;

                eventEntity.Bouts.Add(new Bout
                {
                    OrderIndex = order++,
                    FighterA = fighterA,
                    FighterB = fighterB,
                    WeightClass = fight.WeightClass,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return new EventSyncResult(created, updated, skipped);
    }

    private static Fighter? GetOrCreateFighter(Dictionary<long, Fighter> cache, ApplicationDbContext db, TapologyFighterDto dto)
    {
        var id = ParseFighterId(dto.Link);
        if (id is null) return null;

        if (cache.TryGetValue(id.Value, out var fighter))
        {
            fighter.Name = dto.Name;
            // A card that lists no record shouldn't wipe the one we have.
            fighter.Record = dto.Record ?? fighter.Record;
            return fighter;
        }

        fighter = new Fighter { Id = id.Value, Name = dto.Name, Record = dto.Record, TapologyLink = dto.Link };
        cache[id.Value] = fighter;
        db.Fighters.Add(fighter);
        return fighter;
    }

    // ".../fighters/239144-arlind-berisha" -> 239144
    private static long? ParseFighterId(string link)
    {
        var slug = link.TrimEnd('/').Split('/').Last();
        var idPart = slug.Split('-', 2)[0];
        return long.TryParse(idPart, out var id) ? id : null;
    }

    // "Saturday 09.08.2026 at 07:00 PM ET" -> a real DateTimeOffset.
    // Tapology always normalizes to US Eastern time.
    // Unparseable dates sort last; the loop skips those events anyway.
    private static DateTimeOffset SortDate(string raw)
    {
        try
        {
            return ParseTapologyDate(raw);
        }
        catch (Exception)
        {
            return DateTimeOffset.MaxValue;
        }
    }

    private static DateTimeOffset ParseTapologyDate(string raw)
    {
        var withoutTz = raw.Replace(" ET", "", StringComparison.OrdinalIgnoreCase).Trim();
        var withoutWeekday = withoutTz[(withoutTz.IndexOf(' ') + 1)..]; // "09.08.2026 at 07:00 PM"

        var parsed = DateTime.ParseExact(
            withoutWeekday,
            "MM.dd.yyyy 'at' hh:mm tt",
            CultureInfo.InvariantCulture);

        var offset = EasternTimeZone.GetUtcOffset(parsed);

        // Postgres' "timestamp with time zone" always stores UTC internally
        // and Npgsql requires the DateTimeOffset we hand it to already be
        // UTC (offset 0) - converting here keeps that invariant everywhere.
        return new DateTimeOffset(parsed, offset).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }
}
