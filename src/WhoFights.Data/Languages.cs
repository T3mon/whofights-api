namespace WhoFights.Data;

// The languages the product speaks - the same base codes the frontend's
// language picker uses, mapped to the .NET culture that formats dates and
// times for them. Stored per user (NotificationPreference.Language) and
// read by whatever renders text for that user outside the browser.
public static class Languages
{
    public const string Default = "en";

    public static readonly IReadOnlyDictionary<string, string> Cultures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en-US",
        ["es"] = "es-ES",
        ["pt"] = "pt-BR",
        ["fr"] = "fr-FR",
        ["ru"] = "ru-RU",
        ["uk"] = "uk-UA",
        ["zh"] = "zh-CN",
        ["ar"] = "ar-EG",
    };

    /// <summary>"en-US" → "en"; anything unsupported → "en".</summary>
    public static string Normalize(string? code)
    {
        var baseCode = (code ?? "").Split('-')[0].ToLowerInvariant();
        return Cultures.ContainsKey(baseCode) ? baseCode : Default;
    }
}
