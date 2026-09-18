namespace WhoFights.Api.Models.Api;

/// <summary>
/// The signed-in user's tracked promotions, as the same filter keys the calendar's promotion sidebar uses:
/// a bare promotion code (<c>"UFC"</c>) for the whole promotion, or <c>"CODE::Sub-series"</c>
/// (<c>"UFC::Fight Night"</c>) for one of its sub-series.
/// </summary>
/// <param name="PromotionKeys">Every tracked promotion key. Replacing the whole list is the only write.</param>
public record PromotionFollowsDto(IReadOnlyList<string> PromotionKeys);

/// <summary>The signed-in user's notification settings.</summary>
/// <param name="WeeklyDigestEmail">Every Monday: the coming week's events from the promotions they track.</param>
/// <param name="TimeZone">IANA zone the digest renders in; the calendar sends whatever it's currently showing times in.</param>
public record NotificationPreferencesDto(bool WeeklyDigestEmail, string TimeZone);
