namespace WhoFights.Data.Models.Domain;

// What we notify about. Stored as strings so the DB reads like the UI and
// reordering the enum can never silently remap a row.
public enum NotificationKind
{
    // Monday morning: the coming week's events from the tracked promotions.
    WeeklyDigest,
    // A tracked event starts in about a day - time to adjust plans.
    Reminder24h,
    // A tracked event starts in about an hour - time to find the stream.
    Reminder1h,
}

// How we deliver it. Telegram and push are on the roadmap; the rules in
// NotificationRules decide which of these actually exist today.
public enum NotificationChannel
{
    Email,
    Telegram,
}

// One row per switched-on cell of the Notifications grid (kind x channel).
// No row means off, so a brand-new account receives nothing until it opts
// in, and adding a kind or channel later is just new rows, no schema change.
public class NotificationSubscription
{
    public required string UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public NotificationChannel Channel { get; set; }
}
