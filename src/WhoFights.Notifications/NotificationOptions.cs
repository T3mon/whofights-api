namespace WhoFights.Notifications;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    // Where the links in a notification land. The frontend for event pages
    // and settings; the API for the one-click unsubscribe POST, which email
    // clients call directly with no browser in between.
    public required string FrontendBaseUrl { get; init; }
    public required string ApiBaseUrl { get; init; }

    // Treat every kind as due right now, whatever the clock says. For trying
    // things out by hand (a manual Render run, a local docker compose run) -
    // never set in the scheduled service's environment. The sent-log still
    // applies, so a forced run only reaches people who haven't had it yet.
    public bool Force { get; init; }
}
