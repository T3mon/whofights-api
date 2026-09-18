using WhoFights.Data.Models.Domain;
using WhoFights.Notifications.Localization;

namespace WhoFights.Notifications.Channels;

// Who a notification goes to, with everything a channel needs to address
// and phrase it. Built once per user per run.
public record Recipient(
    string UserId,
    string Email,
    NotificationLocale Locale,
    TimeZoneInfo Zone,
    RecipientLinks Links);

// Every link a notification may carry, resolved for this user (the
// unsubscribe ones embed their personal token).
public record RecipientLinks(
    string Calendar,
    string ManagePromotions,
    string UnsubscribePage,
    string UnsubscribeEndpoint,
    Func<Event, string> Event);

// What gets said, independent of how. One subtype per NotificationKind;
// channels pattern-match on the subtype they know how to render.
public abstract record NotificationContent(NotificationKind Kind, string Reference);

/// <param name="WeekStart">Monday of the week in the recipient's zone - also the log reference.</param>
/// <param name="Events">The week's not-yet-started events from the promotions they track, in start order.</param>
public record DigestContent(DateOnly WeekStart, IReadOnlyList<Event> Events)
    : NotificationContent(NotificationKind.WeeklyDigest, WeekStart.ToString("yyyy-MM-dd"));

// A delivery route. Adding Telegram or push means one new implementation
// registered in Program.cs - the runner, the rules and the content stay.
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    Task SendAsync(NotificationContent content, Recipient recipient, CancellationToken ct);
}
