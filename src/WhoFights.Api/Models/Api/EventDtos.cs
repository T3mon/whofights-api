namespace WhoFights.Api.Models.Api;

/// <summary>A combat sports promotion, e.g. UFC or ONE Championship.</summary>
/// <param name="Id">Internal database id.</param>
/// <param name="Code">Short code as used by Tapology, e.g. "UFC", "ONE", "BKFC". Use this to filter <c>/api/events</c>.</param>
/// <param name="Name">Full promotion name, e.g. "Ultimate Fighting Championship".</param>
public record PromotionDto(int Id, string Code, string Name);

/// <summary>One matchup within an event's fight card.</summary>
/// <param name="FighterA">First fighter's name.</param>
/// <param name="FighterALink">First fighter's Tapology profile page.</param>
/// <param name="FighterB">Second fighter's name.</param>
/// <param name="FighterBLink">Second fighter's Tapology profile page.</param>
/// <param name="WeightClass">Weight class, e.g. "155 lbs". Null if Tapology didn't list one.</param>
public record BoutDto(string FighterA, string FighterALink, string FighterB, string FighterBLink, string? WeightClass);

/// <summary>
/// One row in the calendar feed. Carries only the headline bout, not the
/// full card - fetch <c>/api/events/{slug}</c> for every matchup on the card.
/// </summary>
/// <param name="Id">Internal database id.</param>
/// <param name="Slug">Tapology's URL slug for this event. Pass this to <c>/api/events/{slug}</c> for the full card.</param>
/// <param name="Title">Event name, e.g. "UFC 331: Van vs. Pantoja 2".</param>
/// <param name="Promotion">The promotion running this event.</param>
/// <param name="StartsAt">Start time in UTC.</param>
/// <param name="Venue">Venue name, if known.</param>
/// <param name="Location">City/region/country, if known.</param>
/// <param name="Link">Tapology's page for this event.</param>
/// <param name="SubSeries">
/// The promotion's named sub-series this event belongs to, e.g. "Fight Night" or "Friday Fights" - derived
/// from the title, not scraped data. Null means a flagship/numbered event with no named sub-series (e.g. "UFC 331").
/// </param>
/// <param name="MainEvent">The first-listed bout on the card (usually the main event). Null if the card has no bouts yet.</param>
/// <param name="BoutCount">Total number of bouts on the full card.</param>
public record EventListItemDto(
    int Id,
    string Slug,
    string Title,
    PromotionDto Promotion,
    DateTimeOffset StartsAt,
    string? Venue,
    string? Location,
    string Link,
    string? SubSeries,
    BoutDto? MainEvent,
    int BoutCount);

/// <summary>Full detail for a single event, including every bout on the card.</summary>
/// <param name="Id">Internal database id.</param>
/// <param name="Slug">Tapology's URL slug for this event.</param>
/// <param name="Title">Event name, e.g. "UFC 331: Van vs. Pantoja 2".</param>
/// <param name="Promotion">The promotion running this event.</param>
/// <param name="StartsAt">Start time in UTC.</param>
/// <param name="Venue">Venue name, if known.</param>
/// <param name="Location">City/region/country, if known.</param>
/// <param name="Link">Tapology's page for this event.</param>
/// <param name="SubSeries">
/// The promotion's named sub-series this event belongs to, e.g. "Fight Night" or "Friday Fights" - derived
/// from the title, not scraped data. Null means a flagship/numbered event with no named sub-series (e.g. "UFC 331").
/// </param>
/// <param name="Bouts">Every matchup on the card, in card order (index 0 is the main event).</param>
public record EventDetailDto(
    int Id,
    string Slug,
    string Title,
    PromotionDto Promotion,
    DateTimeOffset StartsAt,
    string? Venue,
    string? Location,
    string Link,
    string? SubSeries,
    IReadOnlyList<BoutDto> Bouts);
