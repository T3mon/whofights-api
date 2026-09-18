namespace WhoFights.Data.Models.Domain;

// Per-user settings that apply to every notification, whatever the kind or
// channel. Which notifications are on lives in NotificationSubscription.
public class NotificationPreference
{
    public required string UserId { get; set; }

    // IANA id ("Europe/Kyiv") captured from the calendar when the user
    // saves - notifications pick their moment and render times in this
    // zone, so they match what the calendar shows them.
    public string TimeZone { get; set; } = "UTC";

    // Base language code as the calendar's language picker uses it ("en",
    // "uk"...) - notifications are written in this language.
    public string Language { get; set; } = Languages.Default;

    // Random, unguessable; lets the one-click link in an email switch email
    // notifications off without a sign-in (email clients call it with no
    // session).
    public required string UnsubscribeToken { get; set; }
}
