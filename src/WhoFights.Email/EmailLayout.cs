using System.Net;

namespace WhoFights.Email;

// The one frame every WhoFights email sits in: dark card, purple top bar,
// wordmark, then the "Why am I receiving this email?" footer. Table layout
// and inline styles only - email clients strip <style> blocks and ignore
// most modern CSS. Colors mirror the site's dark theme and the purple of
// the logo so the mail reads as part of the same product.
public static class EmailLayout
{
    public const string Brand = "#863bff";
    public const string TextBright = "#f0f0f0";
    public const string TextBody = "#cbd2dc";
    public const string TextMuted = "#9aa0aa";
    public const string TextFaint = "#6f7683";
    public const string Link = "#b48cff";
    public const string Surface = "#232732";
    public const string Border = "#2c303a";

    private const string Font = "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;";

    /// <param name="bodyHtml">Everything between the wordmark and the footer.</param>
    /// <param name="whyTitle">The footer heading, "Why am I receiving this email?" in the reader's language.</param>
    /// <param name="whyHtml">The footer's answer - already HTML.</param>
    /// <param name="preheader">Plain text shown next to the subject in inbox previews, hidden in the body.</param>
    /// <param name="rightToLeft">Flips the whole layout for RTL languages (Arabic).</param>
    public static string Wrap(string bodyHtml, string whyTitle, string whyHtml, string? preheader = null, bool rightToLeft = false)
    {
        var preheaderHtml = preheader is null
            ? ""
            : $"""<div style="display:none;max-height:0;overflow:hidden;font-size:1px;line-height:1px;color:#14161b;">{WebUtility.HtmlEncode(preheader)}</div>""";
        var dir = rightToLeft ? "rtl" : "ltr";

        return $"""
            {preheaderHtml}
            <table role="presentation" dir="{dir}" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#14161b;padding:32px 12px;{Font}">
              <tr>
                <td align="center">
                  <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;width:100%;background-color:#1c1f26;border-radius:14px;overflow:hidden;">
                    <tr>
                      <td style="background-color:{Brand};height:6px;line-height:6px;font-size:0;">&nbsp;</td>
                    </tr>
                    <tr>
                      <td style="padding:32px 36px 36px;">
                        <div style="font-size:20px;font-weight:700;margin-bottom:24px;">
                          <span style="color:#e63946;">Who</span><span style="color:#e6c200;">Fights</span>
                        </div>
                        {bodyHtml}
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:32px;border-top:1px solid {Border};">
                          <tr>
                            <td style="padding-top:22px;">
                              <p style="margin:0 0 8px;font-size:14px;font-weight:700;color:{TextBright};">{WebUtility.HtmlEncode(whyTitle)}</p>
                              <p style="margin:0;font-size:13px;line-height:1.6;color:{TextMuted};">{whyHtml}</p>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """;
    }

    public static string Button(string label, string href, int fontSize = 15, string padding = "13px 26px") =>
        $"""<a href="{WebUtility.HtmlEncode(href)}" style="display:inline-block;background-color:{Brand};color:#ffffff;font-size:{fontSize}px;font-weight:700;text-decoration:none;padding:{padding};border-radius:8px;">{WebUtility.HtmlEncode(label)}</a>""";

    public static string TextLink(string label, string href) =>
        $"""<a href="{WebUtility.HtmlEncode(href)}" style="color:{Link};text-decoration:none;">{WebUtility.HtmlEncode(label)}</a>""";
}
