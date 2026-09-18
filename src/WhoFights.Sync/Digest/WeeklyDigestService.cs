using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;
using WhoFights.Email;

namespace WhoFights.Sync.Digest;

// Runs right after the Monday sync, so the week's events are as fresh as
// they get. One email per opted-in user: the next 7 days in their own
// timezone, narrowed to the promotions they track using the same key rule
// as the calendar's sidebar.
public class WeeklyDigestService(
    ApplicationDbContext db,
    IEmailSender emailSender,
    IOptions<DigestOptions> options,
    ILogger<WeeklyDigestService> logger)
{
    // A Monday run retried later the same day must not mail everyone twice;
    // anything under a week old counts as "already sent this week".
    private static readonly TimeSpan ResendGuard = TimeSpan.FromDays(6);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - ResendGuard;

        var recipients = await db.NotificationPreferences
            .Where(p => p.WeeklyDigestEmail && (p.WeeklyDigestLastSentAt == null || p.WeeklyDigestLastSentAt < cutoff))
            .Join(db.Users.Where(u => u.EmailConfirmed && u.Email != null),
                p => p.UserId, u => u.Id,
                (p, u) => new { Prefs = p, u.Email })
            .ToListAsync(ct);

        if (recipients.Count == 0)
        {
            logger.LogInformation("Weekly digest: nobody to send to");
            return 0;
        }

        // Every candidate event for every recipient in one query - the
        // widest possible window is "today somewhere on Earth" through 7
        // days later; each user's zone then trims it. The split rule needs
        // the same year-wide view of events the calendar fetches.
        var yearAround = await db.Events
            .Include(e => e.Promotion)
            .Where(e => e.StartsAt >= now.AddYears(-1) && e.StartsAt <= now.AddYears(1))
            .ToListAsync(ct);
        var subSeriesByPromotion = FollowKeys.SubSeriesByPromotion(yearAround);

        var candidates = await db.Events
            .Include(e => e.Promotion)
            .Include(e => e.Bouts).ThenInclude(b => b.FighterA)
            .Include(e => e.Bouts).ThenInclude(b => b.FighterB)
            .Where(e => e.StartsAt >= now.AddDays(-1) && e.StartsAt < now.AddDays(8))
            .ToListAsync(ct);

        var followsByUser = (await db.UserFollows
                .Where(f => f.PromotionId != null)
                .Select(f => new { f.UserId, f.Promotion!.Code, f.SubSeries })
                .ToListAsync(ct))
            .ToLookup(f => f.UserId, f => FollowKeys.ToKey(f.Code, f.SubSeries));

        var sent = 0;
        foreach (var recipient in recipients)
        {
            var zone = ResolveZone(recipient.Prefs.TimeZone);
            var weekStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            var weekEndExclusive = weekStart.AddDays(7);
            var keys = followsByUser[recipient.Prefs.UserId].ToHashSet();

            var events = candidates
                .Where(e => keys.Contains(FollowKeys.ForEvent(e, subSeriesByPromotion)))
                .Where(e =>
                {
                    var day = WeeklyDigestEmail.LocalDate(e, zone);
                    return day >= weekStart && day < weekEndExclusive;
                })
                .ToList();

            if (events.Count == 0)
            {
                // A "nothing this week" email is noise; skip and stamp so the
                // guard above still behaves if the run is retried.
                logger.LogInformation("Weekly digest: no events for {UserId}, skipping", recipient.Prefs.UserId);
                recipient.Prefs.WeeklyDigestLastSentAt = now;
                continue;
            }

            var message = WeeklyDigestEmail.Render(recipient.Email!, events, weekStart, zone, LinksFor(recipient.Prefs));
            try
            {
                await emailSender.SendAsync(message, ct);
                recipient.Prefs.WeeklyDigestLastSentAt = now;
                sent++;
            }
            catch (Exception ex)
            {
                // One bad address must not stop everyone else's digest; the
                // guard leaves this user eligible for a manual re-run.
                logger.LogError(ex, "Weekly digest: send failed for {UserId}", recipient.Prefs.UserId);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Weekly digest: {Sent} sent of {Eligible} eligible", sent, recipients.Count);
        return sent;
    }

    private WeeklyDigestEmail.Links LinksFor(NotificationPreference prefs)
    {
        var frontend = options.Value.FrontendBaseUrl.TrimEnd('/');
        var api = options.Value.ApiBaseUrl.TrimEnd('/');
        var token = Uri.EscapeDataString(prefs.UnsubscribeToken);
        return new WeeklyDigestEmail.Links(
            Calendar: frontend,
            ManagePromotions: $"{frontend}/?account=promotions",
            // A person clicks through to the frontend page, which POSTs to
            // the API; Gmail's one-click button POSTs to the API directly
            // (no browser involved), so the header carries the API URL.
            UnsubscribePage: $"{frontend}/unsubscribe?token={token}",
            UnsubscribeEndpoint: $"{api}/api/notifications/unsubscribe?token={token}",
            Event: e => $"{frontend}/e/{e.TapologySlug}");
    }

    private TimeZoneInfo ResolveZone(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        logger.LogWarning("Weekly digest: unknown time zone {Zone}, falling back to UTC", id);
        return TimeZoneInfo.Utc;
    }
}
