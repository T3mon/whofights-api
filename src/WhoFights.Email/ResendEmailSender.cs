using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WhoFights.Email;

// Resend's send-an-email call is one POST with a JSON body - not worth
// taking on their (still pre-1.0) SDK for that. Swapping providers later
// means writing one new class this size, not fighting someone else's API
// surface.
public class ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options) : IEmailSender
{
    private readonly ResendOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                From: $"{_options.FromName} <{_options.FromAddress}>",
                To: [message.To],
                Subject: message.Subject,
                Html: message.Html,
                Text: message.Text,
                Headers: message.Headers)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Resend returned {(int)response.StatusCode}: {body}");
        }
    }

    private record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
        [property: JsonPropertyName("headers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Headers);
}
