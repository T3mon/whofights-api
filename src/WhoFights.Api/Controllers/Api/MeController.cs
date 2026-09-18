using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhoFights.Api.Models.Api;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;
using WhoFights.Email;

namespace WhoFights.Api.Controllers.Api;

/// <summary>Everything scoped to the signed-in user. Requires a bearer token from WhoFights.Auth.</summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(ApplicationDbContext db) : ControllerBase
{
    // Identity always comes from the token, never from the request - a client
    // can't be trusted to say whose follows it is editing.
    private string CurrentUserId => User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

    /// <summary>The promotions (and sub-series) this user tracks.</summary>
    [HttpGet("follows")]
    public async Task<ActionResult<PromotionFollowsDto>> GetFollows(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var keys = await db.UserFollows
            .Where(f => f.UserId == userId && f.PromotionId != null)
            .Select(f => new { f.Promotion!.Code, f.SubSeries })
            .ToListAsync(ct);

        return Ok(new PromotionFollowsDto(keys.Select(k => FollowKeys.ToKey(k.Code, k.SubSeries)).ToList()));
    }

    /// <summary>
    /// Replaces this user's tracked promotions with the given set. Keys for promotions that don't exist are
    /// dropped silently rather than failing the whole save. Fighter follows are untouched.
    /// </summary>
    [HttpPut("follows")]
    public async Task<ActionResult<PromotionFollowsDto>> PutFollows(PromotionFollowsDto request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var promotionIdsByCode = await db.Promotions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);

        var wanted = request.PromotionKeys
            .Select(FollowKeys.Parse)
            .Where(k => promotionIdsByCode.ContainsKey(k.Code))
            .Distinct()
            .ToList();

        var existing = await db.UserFollows
            .Where(f => f.UserId == userId && f.PromotionId != null)
            .ToListAsync(ct);

        db.RemoveRange(existing);
        db.AddRange(wanted.Select(k => new UserFollow
        {
            UserId = userId,
            PromotionId = promotionIdsByCode[k.Code],
            SubSeries = k.SubSeries,
        }));
        await db.SaveChangesAsync(ct);

        return Ok(new PromotionFollowsDto(wanted.Select(k => FollowKeys.ToKey(k.Code, k.SubSeries)).ToList()));
    }

    /// <summary>The user's notification settings. Everything is off until they save something.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<NotificationPreferencesDto>> GetNotifications(CancellationToken ct)
    {
        var prefs = await db.NotificationPreferences.FindAsync([CurrentUserId], ct);
        return Ok(prefs is null
            ? new NotificationPreferencesDto(false, "UTC", EmailLocale.Default)
            : new NotificationPreferencesDto(prefs.WeeklyDigestEmail, prefs.TimeZone, prefs.Language));
    }

    [HttpPut("notifications")]
    public async Task<ActionResult<NotificationPreferencesDto>> PutNotifications(NotificationPreferencesDto request, CancellationToken ct)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"Unknown time zone '{request.TimeZone}'.");
        }

        var userId = CurrentUserId;
        var prefs = await db.NotificationPreferences.FindAsync([userId], ct);
        if (prefs is null)
        {
            prefs = new NotificationPreference
            {
                UserId = userId,
                UnsubscribeToken = NotificationPreferenceTokens.Generate(),
            };
            db.NotificationPreferences.Add(prefs);
        }

        prefs.WeeklyDigestEmail = request.WeeklyDigestEmail;
        prefs.TimeZone = request.TimeZone;
        prefs.Language = EmailLocale.Normalize(request.Language);
        await db.SaveChangesAsync(ct);

        return Ok(new NotificationPreferencesDto(prefs.WeeklyDigestEmail, prefs.TimeZone, prefs.Language));
    }
}
