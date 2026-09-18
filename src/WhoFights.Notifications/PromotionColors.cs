namespace WhoFights.Notifications;

// Same palette and same fallback hash as promotionColors.ts in
// whofights-web-ui, so the dot next to an event in the email is the color
// the calendar paints it. Two languages, one rule - keep them in step.
public static class PromotionColors
{
    private static readonly Dictionary<string, string> Fixed = new()
    {
        ["UFC"] = "#e63946",
        ["DWCS"] = "#e63946",
        ["UFCBJJ"] = "#e63946",
        ["MATCHROOM"] = "#eab308",
        ["TOP RANK"] = "#eab308",
        ["MVP"] = "#eab308",
        ["ZUFFA"] = "#eab308",
        ["ONE"] = "#14b8a6",
        ["PFL"] = "#3b82f6",
        ["RIZIN"] = "#39ff14",
        ["RAF"] = "#f97316",
        ["BKFC"] = "#f59e0b",
    };

    private static readonly string[] Fallback =
    [
        "#e63946", "#f4a261", "#2a9d8f", "#264653", "#8338ec", "#3a86ff",
        "#ffb703", "#fb5607", "#06d6a0", "#ef476f", "#118ab2", "#9b5de5",
    ];

    public static string For(string promotionCode)
    {
        if (Fixed.TryGetValue(promotionCode, out var color))
        {
            return color;
        }

        // JavaScript's (hash * 31 + charCode) >>> 0, i.e. unsigned 32-bit.
        uint hash = 0;
        foreach (var c in promotionCode)
        {
            hash = unchecked(hash * 31 + c);
        }
        return Fallback[hash % (uint)Fallback.Length];
    }
}
