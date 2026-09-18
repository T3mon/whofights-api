namespace WhoFights.Email;

// One interface for everything this product mails - confirmation links,
// the weekly digest - so switching providers is a single new
// implementation, not a hunt through every call site.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <param name="Text">Plain-text alternative. Gmail scores HTML-only mail as spammier, so anything sent in bulk should set it.</param>
/// <param name="Headers">Extra SMTP headers, e.g. List-Unsubscribe for one-click unsubscribe.</param>
public record EmailMessage(
    string To,
    string Subject,
    string Html,
    string? Text = null,
    IReadOnlyDictionary<string, string>? Headers = null);
