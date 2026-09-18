using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhoFights.Data;

namespace WhoFights.Api.Controllers.Api;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(ApplicationDbContext db) : ControllerBase
{
    /// <summary>
    /// Switches the weekly digest off for whoever the token belongs to. No sign-in: this is what the
    /// Unsubscribe link in the email and Gmail's one-click unsubscribe button call, and neither carries a
    /// session. Idempotent, so a second click is still a 204.
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

        prefs.WeeklyDigestEmail = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
