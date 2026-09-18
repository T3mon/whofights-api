using WhoFights.Data.Models.Domain;

namespace WhoFights.Api.Models.Api;

/// <summary>
/// The signed-in user's tracked promotions, as the same filter keys the calendar's promotion sidebar uses:
/// a bare promotion code (<c>"UFC"</c>) for the whole promotion, or <c>"CODE::Sub-series"</c>
/// (<c>"UFC::Fight Night"</c>) for one of its sub-series.
/// </summary>
/// <param name="PromotionKeys">Every tracked promotion key. Replacing the whole list is the only write.</param>
public record PromotionFollowsDto(IReadOnlyList<string> PromotionKeys);

/// <summary>One switched-on cell of the notifications grid: a kind of notification on a delivery channel.</summary>
public record NotificationCellDto(NotificationKind Kind, NotificationChannel Channel);

/// <summary>The signed-in user's notification settings: the grid of what they get and how, plus the zone and language everything is rendered in.</summary>
/// <param name="TimeZone">IANA zone notifications pick their moment and show times in; the calendar sends whatever it's currently showing.</param>
/// <param name="Language">Language notifications are written in, as the calendar's language code ("en", "uk"...). Unknown codes fall back to English.</param>
/// <param name="Subscriptions">Every switched-on cell. Replacing the whole list is the only write.</param>
/// <param name="Available">Read-only: every cell that can be switched on right now (channel exists and may carry that kind). Anything else is shown locked.</param>
public record NotificationSettingsDto(
    string TimeZone,
    string Language,
    IReadOnlyList<NotificationCellDto> Subscriptions,
    IReadOnlyList<NotificationCellDto>? Available = null);
