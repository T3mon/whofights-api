using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;
using WhoFights.Notifications.Channels;
using WhoFights.Notifications.Digest;
using WhoFights.Notifications.Localization;

namespace WhoFights.Notifications;

// One pass of "deliver whatever is due right now". Runs every half hour;
// each kind has a due-rule in the recipient's own timezone, and the
// NotificationLog makes every (user, kind, channel, reference) a one-time
// thing, so retries and restarts are always safe.
public class NotificationRunner(
    ApplicationDbContext db,
    IEnumerable<INotificationChannel> channels,
    IOptions<NotificationOptions> options,
    ILogger<NotificationRunner> logger)
{
    // Monday morning, local time. Anyone whose Monday 08:00 has passed and
    // who hasn't had this week's digest yet gets it on the next run - so an
    // outage on Monday means a late digest, not a missing one.
    private const int DigestHourLocal = 8;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var channelsByKind = channels.ToDictionary(c => c.Channel);

        var subscriptions = await db.NotificationSubscriptions.ToListAsync(ct);
        var subscribedUserIds = subscriptions.Select(s => s.UserId).Distinct().ToList();
        if (subscribedUserIds.Count == 0)
        {
            logger.LogInformation("Notifications: nobody is subscribed to anything");
            return;
        }

        var recipients = await db.NotificationPreferences
            .Where(p => subscribedUserIds.Contains(p.UserId))
            .Join(db.Users.Where(u => u.EmailConfirmed && u.Email != null),
                p => p.UserId, u => u.Id,
                (p, u) => new { Prefs = p, Email = u.Email! })
            .ToListAsync(ct);

        var subscriptionsByUser = subscriptions.ToLookup(s => s.UserId, s => (s.Kind, s.Channel));
        var followsByUser = (await db.UserFollows
                .Where(f => f.PromotionId != null)
                .Select(f => new { f.UserId, f.Promotion!.Code, f.SubSeries })
                .ToListAsync(ct))
            .ToLookup(f => f.UserId, f => FollowKeys.ToKey(f.Code, f.SubSeries));

        // Everything any recipient could be told about in this run, loaded
        // once: the coming week wherever on Earth they are. The split rule
        // needs the same year-wide view of events the calendar fetches.
        var yearAround = await db.Events.Include(e => e.Promotion)
            .Where(e => e.StartsAt >= now.AddYears(-1) && e.StartsAt <= now.AddYears(1))
            .ToListAsync(ct);
        var subSeriesByPromotion = FollowKeys.SubSeriesByPromotion(yearAround);
        var upcoming = await db.Events
            .Include(e => e.Promotion)
            .Include(e => e.Bouts).ThenInclude(b => b.FighterA)
            .Include(e => e.Bouts).ThenInclude(b => b.FighterB)
            .Where(e => e.StartsAt >= now.AddDays(-1) && e.StartsAt < now.AddDays(8))
            .OrderBy(e => e.StartsAt)
            .ToListAsync(ct);

        var alreadySent = (await db.NotificationLogs
                .Where(l => subscribedUserIds.Contains(l.UserId) && l.SentAt > now.AddDays(-8))
                .Select(l => new { l.UserId, l.Kind, l.Channel, l.Reference })
                .ToListAsync(ct))
            .Select(l => (l.UserId, l.Kind, l.Channel, l.Reference))
            .ToHashSet();

        var sent = 0;
        foreach (var r in recipients)
        {
            var zone = ResolveZone(r.Prefs.TimeZone);
            var recipient = new Recipient(r.Prefs.UserId, r.Email, NotificationLocale.For(r.Prefs.Language), zone, LinksFor(r.Prefs));
            var localNow = TimeZoneInfo.ConvertTime(now, zone);
            var keys = followsByUser[r.Prefs.UserId].ToHashSet();
            var mine = upcoming.Where(e => keys.Contains(FollowKeys.ForEvent(e, subSeriesByPromotion))).ToList();

            foreach (var content in DueContent(localNow, zone, mine))
            {
                foreach (var (kind, channel) in subscriptionsByUser[r.Prefs.UserId])
                {
                    if (kind != content.Kind || !NotificationRules.IsDeliverable(kind, channel)) continue;
                    if (!channelsByKind.TryGetValue(channel, out var sender)) continue;
                    if (!alreadySent.Add((r.Prefs.UserId, kind, channel, content.Reference))) continue;

                    // Logged even when there's nothing to say, so a quiet
                    // week isn't re-evaluated every half hour.
                    db.NotificationLogs.Add(new NotificationLog
                    {
                        UserId = r.Prefs.UserId, Kind = kind, Channel = channel, Reference = content.Reference, SentAt = now,
                    });
                    if (content is DigestContent { Events.Count: 0 })
                    {
                        logger.LogInformation("Notifications: {Kind} for {UserId} has no events, skipping", kind, r.Prefs.UserId);
                        continue;
                    }

                    try
                    {
                        await sender.SendAsync(content, recipient, ct);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        // One bad address must not stop everyone else. The log
                        // row is dropped again so the next run retries this one.
                        logger.LogError(ex, "Notifications: {Kind} via {Channel} failed for {UserId}", kind, channel, r.Prefs.UserId);
                        db.ChangeTracker.Entries<NotificationLog>()
                            .Single(e => e.Entity.UserId == r.Prefs.UserId && e.Entity.Kind == kind && e.Entity.Channel == channel && e.Entity.Reference == content.Reference)
                            .State = EntityState.Detached;
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Notifications: {Sent} sent to {Recipients} subscribed users", sent, recipients.Count);
    }

    // Which notifications this moment calls for. Each kind decides for
    // itself; reminders slot in here when they exist.
    private IEnumerable<NotificationContent> DueContent(DateTimeOffset localNow, TimeZoneInfo zone, List<Event> events)
    {
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // most recent Monday
        var digestDue = options.Value.Force
            || today > weekStart
            || (today == weekStart && localNow.Hour >= DigestHourLocal);
        if (digestDue)
        {
            var weekEnd = weekStart.AddDays(7);
            yield return new DigestContent(weekStart, events
                .Where(e =>
                {
                    var day = WeeklyDigestEmail.LocalDate(e, zone);
                    return day >= today && day < weekEnd;
                })
                .ToList());
        }
    }

    private RecipientLinks LinksFor(NotificationPreference prefs)
    {
        var frontend = options.Value.FrontendBaseUrl.TrimEnd('/');
        var api = options.Value.ApiBaseUrl.TrimEnd('/');
        var token = Uri.EscapeDataString(prefs.UnsubscribeToken);
        return new RecipientLinks(
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

        logger.LogWarning("Notifications: unknown time zone {Zone}, falling back to UTC", id);
        return TimeZoneInfo.Utc;
    }
}
