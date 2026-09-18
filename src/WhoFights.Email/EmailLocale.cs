using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WhoFights.Email;

// The reader's language for an email: strings from Locales/<code>.json (same
// {{placeholder}} and _one/_few/_many/_other conventions as the frontend's
// i18next files, so text can be copied between the two) plus the .NET
// culture for weekday/month names and time formats. Falls back to English
// for anything unknown rather than ever failing a send over a label.
public sealed partial class EmailLocale
{
    public const string Default = "en";

    // Base codes exactly as the frontend registers them; region-tagged
    // variants ("en-US") are normalised down to these.
    private static readonly Dictionary<string, string> Cultures = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly ConcurrentDictionary<string, EmailLocale> Cache = new();

    public string Code { get; }
    public CultureInfo Culture { get; }
    public bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;

    private readonly Dictionary<string, string> _strings;

    private EmailLocale(string code, CultureInfo culture, Dictionary<string, string> strings)
    {
        Code = code;
        Culture = culture;
        _strings = strings;
    }

    public static bool IsSupported(string? code) => code is not null && Cultures.ContainsKey(Normalize(code));

    /// <summary>"en-US" → "en"; anything unsupported → "en".</summary>
    public static string Normalize(string? code)
    {
        var baseCode = (code ?? "").Split('-')[0].ToLowerInvariant();
        return Cultures.ContainsKey(baseCode) ? baseCode : Default;
    }

    public static EmailLocale For(string? code) => Cache.GetOrAdd(Normalize(code), Load);

    private static EmailLocale Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Locales.{code}.json")
            ?? throw new InvalidOperationException($"Embedded locale Locales/{code}.json is missing.");
        using var doc = JsonDocument.Parse(stream);

        // Flatten {"digest": {"title": ...}} to "digest.title" so lookups
        // read like the frontend's t("digest.title").
        var strings = new Dictionary<string, string>();
        foreach (var section in doc.RootElement.EnumerateObject())
        {
            foreach (var entry in section.Value.EnumerateObject())
            {
                strings[$"{section.Name}.{entry.Name}"] = entry.Value.GetString() ?? "";
            }
        }

        return new EmailLocale(code, CultureInfo.GetCultureInfo(Cultures[code]), strings);
    }

    /// <summary>A plain string with {{name}} placeholders filled in.</summary>
    public string T(string key, params (string Name, object Value)[] args)
    {
        var template = _strings.TryGetValue(key, out var s) ? s : For(Default)._strings.GetValueOrDefault(key, key);
        return Placeholder().Replace(template, m =>
        {
            foreach (var (name, value) in args)
            {
                if (name == m.Groups[1].Value) return Convert.ToString(value, Culture) ?? "";
            }
            return m.Value;
        });
    }

    /// <summary>Picks key_one / key_few / key_many / key_other... by the language's CLDR plural rule, with {{n}} = count.</summary>
    public string Plural(string key, int count)
    {
        var category = PluralCategory(count);
        var pluralKey = _strings.ContainsKey($"{key}_{category}") ? $"{key}_{category}" : $"{key}_other";
        return T(pluralKey, ("n", count));
    }

    /// <summary>A date/time in this language, using a pattern from the locale file's "formats" section.</summary>
    public string Date(DateTimeOffset value, string formatKey) => value.ToString(T($"formats.{formatKey}"), Culture);

    public string Date(DateOnly value, string formatKey) => value.ToString(T($"formats.{formatKey}"), Culture);

    public string Time(DateTimeOffset value) => value.ToString(Culture.DateTimeFormat.ShortTimePattern, Culture);

    // CLDR plural categories for the languages we ship. Adding a language
    // with rules not covered here means adding a case.
    private string PluralCategory(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        switch (Code)
        {
            case "ru":
            case "uk":
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "few";
                return "many";
            case "ar":
                if (n == 0) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                if (mod100 is >= 3 and <= 10) return "few";
                if (mod100 is >= 11 and <= 99) return "many";
                return "other";
            case "fr":
            case "pt":
                return n is 0 or 1 ? "one" : "other";
            case "zh":
                return "other";
            default:
                return n == 1 ? "one" : "other";
        }
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
