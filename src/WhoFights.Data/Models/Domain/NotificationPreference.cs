namespace WhoFights.Data.Models.Domain;

// One row per user who has ever touched their notification settings.
// Absence means "all off", so nobody gets mail without opting in.
public class NotificationPreference
{
    public required string UserId { get; set; }

    public bool WeeklyDigestEmail { get; set; }

    // IANA id ("Europe/Kyiv") captured from the calendar when the user
    // saves - the digest renders event times and picks its 7-day window in
    // this zone, so it matches what the calendar shows them.
    public string TimeZone { get; set; } = "UTC";

    // Random, unguessable; lets the one-click link in an email switch the
    // digest off without a sign-in (email clients call it with no session).
    public required string UnsubscribeToken { get; set; }

    // Guards against double-sending when a Monday run is retried by hand.
    public DateTimeOffset? WeeklyDigestLastSentAt { get; set; }
}
