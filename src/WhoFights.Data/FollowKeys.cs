using WhoFights.Data.Models.Domain;

namespace WhoFights.Data;

// The promotion filter keys shared with the calendar's sidebar (eventSeries.ts
// in whofights-web-ui): a bare code ("UFC") or "CODE::Sub-series"
// ("UFC::Fight Night"). Tracked promotions are stored and matched with the
// same rule the sidebar filters by, so an email digest and the calendar
// always agree on which events belong to which key.
public static class FollowKeys
{
    private const string Separator = "::";

    public static string ToKey(string promotionCode, string? subSeries) =>
        subSeries is null ? promotionCode : promotionCode + Separator + subSeries;

    public static (string Code, string? SubSeries) Parse(string key)
    {
        var at = key.IndexOf(Separator, StringComparison.Ordinal);
        return at < 0 ? (key, null) : (key[..at], key[(at + Separator.Length)..]);
    }

    // A promotion is "split" once its events show 2+ distinct sub-series
    // values (UFC has both null/flagship and "Fight Night" events). Only
    // then does an event's own sub-series become part of its key; an
    // unsplit promotion is one key for everything. Mirrors
    // computeSubSeriesByPromotion + filterKeyForEvent on the frontend.
    public static Dictionary<string, HashSet<string?>> SubSeriesByPromotion(IEnumerable<Event> events)
    {
        var map = new Dictionary<string, HashSet<string?>>();
        foreach (var e in events)
        {
            if (!map.TryGetValue(e.Promotion.Code, out var set))
            {
                set = [];
                map[e.Promotion.Code] = set;
            }
            set.Add(e.SubSeries);
        }
        return map;
    }

    public static string ForEvent(Event e, Dictionary<string, HashSet<string?>> subSeriesByPromotion)
    {
        var split = subSeriesByPromotion.TryGetValue(e.Promotion.Code, out var set) && set.Count >= 2;
        return split ? ToKey(e.Promotion.Code, e.SubSeries) : e.Promotion.Code;
    }
}
