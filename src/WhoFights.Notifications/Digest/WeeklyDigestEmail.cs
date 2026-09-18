using System.Net;
using System.Text;
using WhoFights.Data.Models.Domain;
using WhoFights.Email;
using WhoFights.Notifications.Channels;
using WhoFights.Notifications.Localization;

namespace WhoFights.Notifications.Digest;

// Renders one user's digest as an email, in their language: a 7-day strip
// with a dot per event, then the week as a timeline grouped by day - time
// on the left, a bar in the promotion's colour, title, main event,
// location. Everything on one screen, no headliner spotlight.
public static class WeeklyDigestEmail
{
    public static EmailMessage Render(DigestContent digest, Recipient recipient)
    {
        var (to, locale, zone, links) = (recipient.Email, recipient.Locale, recipient.Zone, recipient.Links);
        var weekStart = digest.WeekStart;
        var ordered = digest.Events.OrderBy(e => e.StartsAt).ToList();
        var range = $"{locale.Date(weekStart, "monthDay")} – {locale.Date(weekStart.AddDays(6), "monthDay")}";
        var count = locale.Plural("digest.events", ordered.Count);
        var summary = locale.T("digest.summary", ("count", count));
        var zoneLabel = ZoneLabel(zone);
        var subject = locale.T("digest.subject", ("count", count), ("range", range));
        var preheader = string.Join(", ", ordered.Take(3).Select(e => ShortMatchup(e, locale))) + (ordered.Count > 3 ? "…" : "");

        var body = new StringBuilder();
        body.Append($"""
            <h1 style="margin:0 0 4px;font-size:26px;line-height:1.3;font-weight:700;color:{EmailLayout.TextBright};">{Html(locale.T("digest.title"))}</h1>
            <p style="margin:0 0 18px;font-size:14px;line-height:1.6;color:{EmailLayout.TextMuted};">{Html(range)} &middot; {Html(summary)}</p>
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
                    <div style="font-size:10px;font-weight:700;letter-spacing:0.06em;color:{EmailLayout.TextMuted};">{Html(day.ToString("ddd", locale.Culture).ToUpper(locale.Culture))}</div>
                    <div style="font-size:17px;font-weight:800;color:{(dayEvents.Count > 0 ? EmailLayout.TextBright : EmailLayout.TextFaint)};margin:2px 0 4px;">{day.Day}</div>
                    <div style="height:6px;line-height:6px;font-size:0;">{(dots.Length > 0 ? dots : "&nbsp;")}</div>
                  </div>
                </td>
                """);
        }

        body.Append("</tr></table>");
        body.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:6px;">""");

        // The bar sits on the side the text starts from, with the gutter
        // between time and bar mirrored to match.
        var barSide = locale.IsRightToLeft ? "right" : "left";
        var timePadding = locale.IsRightToLeft ? "8px 0 8px 12px" : "8px 12px 8px 0";
        var textPadding = locale.IsRightToLeft ? "8px 12px 8px 0" : "8px 0 8px 12px";
        foreach (var dayGroup in ordered.GroupBy(e => LocalDate(e, zone)))
        {
            body.Append($"""
                <tr><td colspan="2" style="padding:18px 0 6px;font-size:12px;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:{EmailLayout.TextMuted};">{Html(locale.Date(dayGroup.Key, "dayHeading").ToUpper(locale.Culture))}</td></tr>
                """);
            foreach (var e in dayGroup)
            {
                var local = TimeZoneInfo.ConvertTime(e.StartsAt, zone);
                var weight = MainEvent(e)?.WeightClass;
                var weightHtml = weight is null ? "" : $""" <span style="color:{EmailLayout.TextFaint};">&middot; {Html(weight)}</span>""";
                var location = ShortLocation(e.Location);
                var locationHtml = location.Length > 0 ? $"{Html(location)} &middot; " : "";
                body.Append($"""
                    <tr>
                      <td width="52" valign="top" style="padding:{timePadding};font-size:13px;font-weight:700;color:{EmailLayout.TextBody};white-space:nowrap;">{Html(locale.Time(local))}</td>
                      <td valign="top" style="padding:{textPadding};border-{barSide}:3px solid {PromotionColors.For(e.Promotion.Code)};">
                        <div style="font-size:15px;font-weight:700;color:{EmailLayout.TextBright};line-height:1.3;">{Html(e.Title)}</div>
                        <div style="font-size:13px;color:{EmailLayout.TextBody};line-height:1.5;margin-top:2px;">{Html(Matchup(e, locale))}{weightHtml}</div>
                        <div style="font-size:12px;color:{EmailLayout.TextFaint};line-height:1.5;">{locationHtml}{EmailLayout.TextLink(locale.T("digest.fullCard"), links.Event(e))}</div>
                      </td>
                    </tr>
                    """);
            }
        }

        body.Append("</table>");
        body.Append($"""<div style="margin-top:28px;">{EmailLayout.Button(locale.T("digest.openCalendar"), links.Calendar)}</div>""");

        var why = $"{Html(locale.T("digest.why", ("zone", zoneLabel)))} "
                  + $"{EmailLayout.TextLink(locale.T("digest.managePromotions"), links.ManagePromotions)} &middot; {EmailLayout.TextLink(locale.T("digest.unsubscribe"), links.UnsubscribePage)}";

        var text = new StringBuilder()
            .AppendLine($"WhoFights - {locale.T("digest.title")}")
            .AppendLine($"{range} - {summary}")
            .AppendLine();
        foreach (var e in ordered)
        {
            var local = TimeZoneInfo.ConvertTime(e.StartsAt, zone);
            text.AppendLine($"{locale.Date(local, "shortDay")} {locale.Time(local)}  {e.Title}");
            text.AppendLine($"  {string.Join(" - ", new[] { Matchup(e, locale), ShortLocation(e.Location) }.Where(s => s.Length > 0))}");
            text.AppendLine($"  {links.Event(e)}");
            text.AppendLine();
        }
        text.AppendLine($"{locale.T("digest.openCalendar")}: {links.Calendar}")
            .AppendLine()
            .AppendLine(locale.T("digest.why", ("zone", zoneLabel)))
            .AppendLine($"{locale.T("digest.managePromotions")}: {links.ManagePromotions}")
            .AppendLine($"{locale.T("digest.unsubscribe")}: {links.UnsubscribePage}");

        return new EmailMessage(
            to,
            subject,
            EmailLayout.Wrap(body.ToString(), locale.T("digest.whyTitle"), why, preheader, locale.IsRightToLeft),
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
    private static string Matchup(Event e, NotificationLocale locale) =>
        MainEvent(e) is { } m ? $"{m.FighterA.Name} {locale.T("digest.vs")} {m.FighterB.Name}" : e.Title;

    private static string ShortMatchup(Event e, NotificationLocale locale) =>
        MainEvent(e) is { } m ? $"{Surname(m.FighterA.Name)} {locale.T("digest.vs")} {Surname(m.FighterB.Name)}" : e.Title;

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
