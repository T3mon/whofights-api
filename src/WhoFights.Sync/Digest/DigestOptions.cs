namespace WhoFights.Sync.Digest;

public class DigestOptions
{
    public const string SectionName = "Digest";

    // Where the links in the email land. The frontend for event pages and
    // settings; the API for the one-click unsubscribe POST, which email
    // clients call directly with no browser in between.
    public required string FrontendBaseUrl { get; init; }
    public required string ApiBaseUrl { get; init; }

    // Send today regardless of weekday. For trying the digest out by hand
    // (a manual Render run or a local docker compose run) - never set in a
    // scheduled service's environment.
    public bool Force { get; init; }
}
