namespace WhoFights.Data.Models.Domain;

// Every delivery, once. The notifications job runs every half hour and
// asks "is this due and not yet sent?" - this table is the "not yet sent"
// half, so a crashed or re-run job can never mail anyone twice.
public class NotificationLog
{
    public long Id { get; set; }

    public required string UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public NotificationChannel Channel { get; set; }

    // What this delivery was about: the week's Monday ("2026-09-21") for a
    // digest, the event id for a reminder. Unique together with the three
    // columns above.
    public required string Reference { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
