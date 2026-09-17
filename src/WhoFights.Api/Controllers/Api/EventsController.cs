using WhoFights.Data;
using WhoFights.Api.Models.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WhoFights.Api.Controllers.Api;

[ApiController]
[Route("api/events")]
public class EventsController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>
    /// Lists upcoming events for the calendar view, soonest first. Each row carries only the headline bout - call
    /// GET /api/events/{slug} for the full card.
    /// </summary>
    /// <param name="from">Only include events starting on or after this time. Defaults to now.</param>
    /// <param name="to">Only include events starting on or before this time. Defaults to one year after <paramref name="from"/>.</param>
    /// <param name="promotions">
    /// Comma-separated promotion codes to filter by, e.g. <c>UFC,ONE,BKFC</c> (see <c>/api/promotions</c> for the
    /// full list of codes). Omit to include every promotion.
    /// </param>
    /// <param name="take">Maximum number of events to return. Defaults to 200, capped at 500.</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventListItemDto>>> GetEvents(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? promotions,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var rangeStart = from ?? DateTimeOffset.UtcNow;
        var rangeEnd = to ?? rangeStart.AddYears(1);

        var query = db.Events
            .Where(e => e.StartsAt >= rangeStart && e.StartsAt <= rangeEnd);

        if (!string.IsNullOrWhiteSpace(promotions))
        {
            var codes = promotions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant())
                .ToArray();
            query = query.Where(e => codes.Contains(e.Promotion.Code));
        }

        var events = await query
            .OrderBy(e => e.StartsAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(e => new EventListItemDto(
                e.Id,
                e.TapologySlug,
                e.Title,
                new PromotionDto(e.Promotion.Id, e.Promotion.Code, e.Promotion.Name),
                e.StartsAt,
                e.Venue,
                e.Location,
                e.TapologyLink,
                e.SubSeries,
                e.Bouts
                    .OrderBy(b => b.OrderIndex)
                    .Select(b => new BoutDto(b.FighterA.Name, b.FighterA.TapologyLink, b.FighterB.Name, b.FighterB.TapologyLink, b.WeightClass))
                    .FirstOrDefault(),
                e.Bouts.Count))
            .ToListAsync(ct);

        return Ok(events);
    }

    /// <summary>Gets the full fight card for one event.</summary>
    /// <param name="slug">The event's Tapology slug, from the <c>slug</c> field of a <c>/api/events</c> result (e.g. <c>147320-ufc-334</c>).</param>
    /// <response code="404">No event matches that slug.</response>
    [HttpGet("{slug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailDto>> GetEvent(string slug, CancellationToken ct)
    {
        var eventDetail = await db.Events
            .Where(e => e.TapologySlug == slug)
            .Select(e => new EventDetailDto(
                e.Id,
                e.TapologySlug,
                e.Title,
                new PromotionDto(e.Promotion.Id, e.Promotion.Code, e.Promotion.Name),
                e.StartsAt,
                e.Venue,
                e.Location,
                e.TapologyLink,
                e.SubSeries,
                e.Bouts
                    .OrderBy(b => b.OrderIndex)
                    .Select(b => new BoutDto(b.FighterA.Name, b.FighterA.TapologyLink, b.FighterB.Name, b.FighterB.TapologyLink, b.WeightClass))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return eventDetail is null ? NotFound() : Ok(eventDetail);
    }
}
