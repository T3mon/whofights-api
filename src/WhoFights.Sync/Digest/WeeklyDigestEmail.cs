using System.Globalization;
using System.Net;
using System.Text;
using WhoFights.Data.Models.Domain;
using WhoFights.Email;

namespace WhoFights.Sync.Digest;

// Renders one user's digest: a Mon-Sun strip with a dot per event, then
// every event of the week as a compact row. Everything on one screen -
// the decision was to keep it tight rather than spotlight a headliner.
public static class WeeklyDigestEmail
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public record Links(string Calendar, string ManagePromotions, string UnsubscribePage, string UnsubscribeEndpoint, Func<Event, string> Event);

    public static EmailMessage Render(string to, IReadOnlyList<Event> events, DateOnly weekStart, TimeZoneInfo zone, Links links)
    {
        var ordered = events.OrderBy(e => e.StartsAt).ToList();
        var weekEnd = weekStart.AddDays(6);
        var range = weekStart.Month == weekEnd.Month
            ? $"{weekStart.ToString("MMM d", Culture)} – {weekEnd.Day}"
            : $"{weekStart.ToString("MMM d", Culture)} – {weekEnd.ToString("MMM d", Culture)}";
        // Entity in the HTML so a client that guesses the wrong charset can't mangle the dash.
        var rangeHtml = range.Replace("–", "&ndash;");
        var promotions = ordered.Select(e => e.Promotion.Code).Distinct().Count();
        var count = $"{ordered.Count} {(ordered.Count == 1 ? "event" : "events")}";
        var summary = $"{count} from the {(promotions == 1 ? "promotion" : "promotions")} you track";
        var zoneLabel = ZoneLabel(zone);

        var subject = $"Weekly outlook: {count}, {range}";
        var preheader = string.Join(", ", ordered.Take(3).Select(ShortMatchup)) + (ordered.Count > 3 ? "…" : "");

        var body = new StringBuilder();
        body.Append($"""
            <h1 style="margin:0 0 4px;font-size:26px;line-height:1.3;font-weight:700;color:{EmailLayout.TextBright};">Weekly outlook</h1>
            <p style="margin:0 0 18px;font-size:14px;line-height:1.6;color:{EmailLayout.TextMuted};">{rangeHtml} &middot; {summary}</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
            """);

        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var dayEvents = ordered.Where(e => LocalDate(e, zone) == day).ToList();
            var dots = string.Concat(dayEvents.Take(4).Select(e => Dot(e, 6, "0 1px")));
            body.Append($"""
                <td width="14%" align="center" style="padding:0 2px;">
                  <div style="background-color:{(dayEvents.Count > 0 ? "#2a2140" : EmailLayout.Surface)};border-radius:8px;padding:8px 0 6px;">
                    <div style="font-size:10px;font-weight:700;letter-spacing:0.06em;color:{EmailLayout.TextMuted};">{day.ToString("ddd", Culture).ToUpperInvariant()}</div>
                    <div style="font-size:17px;font-weight:800;color:{(dayEvents.Count > 0 ? EmailLayout.TextBright : EmailLayout.TextFaint)};margin:2px 0 4px;">{day.Day}</div>
                    <div style="height:6px;line-height:6px;font-size:0;">{(dots.Length > 0 ? dots : "&nbsp;")}</div>
                  </div>
                </td>
                """);
        }

        body.Append("</tr></table>");
        body.Append($"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:22px;">""");

        foreach (var e in ordered)
        {
            var local = TimeZoneInfo.ConvertTime(e.StartsAt, zone);
            var sub = string.Join(" &middot; ", new[] { Html(e.Title), Html(ShortLocation(e.Location)) }.Where(s => s.Length > 0));
            body.Append($"""
                <tr>
                  <td valign="top" style="padding:10px 0;border-bottom:1px solid {EmailLayout.Border};font-size:12px;font-weight:700;color:{EmailLayout.TextMuted};white-space:nowrap;">{local.ToString("ddd d", Culture)}<br><span style="font-weight:400;">{local.ToString("h:mm tt", Culture)}</span></td>
                  <td valign="top" style="padding:10px 12px;border-bottom:1px solid {EmailLayout.Border};">
                    <div style="font-size:14px;font-weight:700;color:{EmailLayout.TextBright};">{Dot(e, 8, "0 7px 0 0")}{Html(Matchup(e))}</div>
                    <div style="font-size:12px;color:{EmailLayout.TextMuted};margin-top:2px;">{sub}</div>
                  </td>
                  <td valign="middle" align="right" style="padding:10px 0;border-bottom:1px solid {EmailLayout.Border};white-space:nowrap;"><a href="{Html(links.Event(e))}" style="font-size:12px;font-weight:700;color:{EmailLayout.Link};text-decoration:none;">Card &rarr;</a></td>
                </tr>
                """);
        }

        body.Append("</table>");
        body.Append($"""<div style="margin-top:26px;">{EmailLayout.Button("Open the calendar", links.Calendar)}</div>""");

        var why = $"You track these promotions on WhoFights, so every Monday you get the week ahead. Times are shown in {Html(zoneLabel)}. "
                  + $"{EmailLayout.TextLink("Manage tracked promotions", links.ManagePromotions)} &middot; {EmailLayout.TextLink("Unsubscribe", links.UnsubscribePage)}";

        var text = new StringBuilder()
            .AppendLine("WhoFights - Weekly outlook")
            .AppendLine($"{range} - {summary}")
            .AppendLine();
        foreach (var e in ordered)
        {
            var local = TimeZoneInfo.ConvertTime(e.StartsAt, zone);
            text.AppendLine($"{local.ToString("ddd MMM d h:mm tt", Culture)}  {Matchup(e)}");
            text.AppendLine($"  {string.Join(" - ", new[] { e.Title, ShortLocation(e.Location) }.Where(s => s.Length > 0))}");
            text.AppendLine($"  {links.Event(e)}");
            text.AppendLine();
        }
        text.AppendLine($"Open the calendar: {links.Calendar}")
            .AppendLine()
            .AppendLine($"You track these promotions on WhoFights, so every Monday you get the week ahead. Times in {zoneLabel}.")
            .AppendLine($"Manage tracked promotions: {links.ManagePromotions}")
            .AppendLine($"Unsubscribe: {links.UnsubscribePage}");

        return new EmailMessage(
            to,
            subject,
            EmailLayout.Wrap(body.ToString(), why, preheader),
            text.ToString(),
            new Dictionary<string, string>
            {
                // RFC 8058: lets Gmail/Yahoo show their own unsubscribe button
                // and POST straight to the API without a browser.
                ["List-Unsubscribe"] = $"<{links.UnsubscribeEndpoint}>",
                ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
            });
    }

    public static DateOnly LocalDate(Event e, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.StartsAt, zone).DateTime);

    private static Bout? MainEvent(Event e) => e.Bouts.OrderBy(b => b.OrderIndex).FirstOrDefault();

    // Same as matchupLabel in calendarData.ts: fighters if we know them, else the title.
    private static string Matchup(Event e) =>
        MainEvent(e) is { } m ? $"{m.FighterA.Name} vs {m.FighterB.Name}" : e.Title;

    private static string ShortMatchup(Event e) =>
        MainEvent(e) is { } m ? $"{Surname(m.FighterA.Name)} vs {Surname(m.FighterB.Name)}" : e.Title;

    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase) { "jr.", "jr", "sr.", "sr", "ii", "iii", "iv" };

    private static string Surname(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1 && Suffixes.Contains(parts[^1]))
        {
            parts.RemoveAt(parts.Count - 1);
        }
        return parts.Count > 0 ? parts[^1] : name;
    }

    // Same as shortLocation in calendarData.ts: "Las Vegas, Nevada, United States" -> "Las Vegas, Nevada".
    private static string ShortLocation(string? location) =>
        location is null ? "" : string.Join(", ", location.Split(',', StringSplitOptions.TrimEntries).Take(2));

    private static string ZoneLabel(TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(DateTimeOffset.UtcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{zone.Id} (UTC{sign}{offset:h\\:mm})";
    }

    private static string Dot(Event e, int size, string margin) =>
        $"""<span style="display:inline-block;width:{size}px;height:{size}px;border-radius:50%;background-color:{PromotionColors.For(e.Promotion.Code)};margin:{margin};"></span>""";

    private static string Html(string s) => WebUtility.HtmlEncode(s);
}
