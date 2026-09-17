using System.Text.Json.Serialization;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using WhoFights.Auth.Options;

namespace WhoFights.Auth.Services;

public record GoogleIdentity(string Subject, string Email);

public class InvalidGoogleTokenException : Exception;

/// <summary>
/// Turns whichever token Google handed the frontend into a verified (subject, email) pair. ID tokens come from
/// Google's own rendered button; access tokens come from our custom "Continue with Google" button, which uses the
/// OAuth popup flow because Google only lets custom-styled buttons obtain that kind of token.
/// </summary>
public class GoogleTokenVerifier(HttpClient httpClient, IOptions<GoogleOptions> googleOptions)
{
    public async Task<GoogleIdentity> VerifyIdTokenAsync(string idToken)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [googleOptions.Value.ClientId],
            });
            return new GoogleIdentity(payload.Subject, payload.Email);
        }
        catch (Exception e) when (e is not HttpRequestException)
        {
            // Malformed input surfaces as FormatException/JSON errors rather than
            // InvalidJwtException; all of them mean "not a token we accept". Only a
            // failure to reach Google's certificate endpoint is genuinely our problem.
            throw new InvalidGoogleTokenException();
        }
    }

    public async Task<GoogleIdentity> VerifyAccessTokenAsync(string accessToken, CancellationToken ct)
    {
        // tokeninfo (unlike userinfo) reports which client the token was issued to, which is
        // what stops an access token minted for some other app from signing in here.
        using var response = await httpClient.GetAsync(
            $"tokeninfo?access_token={Uri.EscapeDataString(accessToken)}", ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidGoogleTokenException();
        }

        var info = await response.Content.ReadFromJsonAsync<TokenInfo>(ct);
        if (info is null
            || info.Audience != googleOptions.Value.ClientId
            || string.IsNullOrEmpty(info.Subject)
            || string.IsNullOrEmpty(info.Email)
            || info.EmailVerified != "true")
        {
            throw new InvalidGoogleTokenException();
        }

        return new GoogleIdentity(info.Subject, info.Email);
    }

    private sealed record TokenInfo(
        [property: JsonPropertyName("aud")] string? Audience,
        [property: JsonPropertyName("sub")] string? Subject,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("email_verified")] string? EmailVerified);
}
