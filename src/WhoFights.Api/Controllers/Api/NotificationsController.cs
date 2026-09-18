using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;

namespace WhoFights.Api.Controllers.Api;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>
    /// Switches every email notification off for whoever the token belongs to. No sign-in: this is what
    /// the Unsubscribe link in the email and Gmail's one-click unsubscribe button call, and neither
    /// carries a session. Idempotent, so a second click is still a 204.
    /// </summary>
    [HttpPost("unsubscribe")]
    [AllowAnonymous]
    public async Task<IActionResult> Unsubscribe([FromQuery] string token, CancellationToken ct)
    {
        var prefs = await db.NotificationPreferences.SingleOrDefaultAsync(p => p.UnsubscribeToken == token, ct);
        if (prefs is null)
        {
            return NotFound();
        }

        db.NotificationSubscriptions.RemoveRange(
            db.NotificationSubscriptions.Where(s => s.UserId == prefs.UserId && s.Channel == NotificationChannel.Email));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
