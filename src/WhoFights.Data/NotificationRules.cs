using WhoFights.Data.Models.Domain;

namespace WhoFights.Data;

// The one place that says which channel may carry which kind. The API
// uses it to refuse a checkbox that makes no sense, the frontend renders
// the grid from it, and the notifications job only delivers what it allows
// - so an email reminder can't be switched on by anyone, anywhere.
public static class NotificationRules
{
    // Reminders are time-critical and interruptive - that's what instant
    // channels are for. Email is for the calm weekly overview only.
    private static readonly Dictionary<NotificationChannel, NotificationKind[]> Deliverable = new()
    {
        [NotificationChannel.Email] = [NotificationKind.WeeklyDigest],
        [NotificationChannel.Telegram] = [NotificationKind.WeeklyDigest, NotificationKind.Reminder24h, NotificationKind.Reminder1h],
    };

    // Channels that actually exist today. Telegram joins once the bot and
    // the account-linking flow are built; until then its cells stay locked.
    public static readonly IReadOnlySet<NotificationChannel> AvailableChannels = new HashSet<NotificationChannel> { NotificationChannel.Email };

    public static bool IsDeliverable(NotificationKind kind, NotificationChannel channel) =>
        Deliverable.TryGetValue(channel, out var kinds) && kinds.Contains(kind);

    public static bool IsAvailable(NotificationKind kind, NotificationChannel channel) =>
        AvailableChannels.Contains(channel) && IsDeliverable(kind, channel);

    /// <summary>Every cell a user may switch on right now.</summary>
    public static IEnumerable<(NotificationKind Kind, NotificationChannel Channel)> AvailableCells() =>
        from channel in AvailableChannels
        from kind in Deliverable[channel]
        select (kind, channel);
}
