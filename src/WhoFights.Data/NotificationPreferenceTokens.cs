using System.Security.Cryptography;

namespace WhoFights.Data;

public static class NotificationPreferenceTokens
{
    // 32 random bytes, URL-safe base64: long enough that guessing one is
    // hopeless, short enough to survive being wrapped in an email footer.
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
