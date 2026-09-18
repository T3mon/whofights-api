namespace WhoFights.Data.Models.Domain;

// A follow is either a whole promotion or a single fighter, never both -
// enforced by a DB check constraint (see ApplicationDbContext.OnModelCreating).
public class UserFollow
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public int? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    // Narrows a promotion follow to one of its sub-series ("Fight Night",
    // "Friday Fights"...). Same keys and same rule as the calendar's own
    // promotion filter (filterKeyForEvent in whofights-web-ui): null means
    // the whole promotion, except for a promotion whose events span 2+
    // sub-series, where null is only its flagship/numbered events.
    public string? SubSeries { get; set; }

    public long? FighterId { get; set; }
    public Fighter? Fighter { get; set; }
}
