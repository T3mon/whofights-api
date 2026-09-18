using WhoFights.Data.Models.Domain;
using WhoFights.Email;
using WhoFights.Notifications.Digest;

namespace WhoFights.Notifications.Channels;

// Email carries the weekly digest only (see NotificationRules) - the runner
// never hands it anything else, so an unknown content type here is a
// programming error, not a user-facing case.
public class EmailChannel(IEmailSender emailSender) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(NotificationContent content, Recipient recipient, CancellationToken ct) => content switch
    {
        DigestContent digest => emailSender.SendAsync(WeeklyDigestEmail.Render(digest, recipient), ct),
        _ => throw new NotSupportedException($"Email cannot deliver {content.Kind}."),
    };
}
