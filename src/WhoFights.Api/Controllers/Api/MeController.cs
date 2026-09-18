using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhoFights.Api.Models.Api;
using WhoFights.Data;
using WhoFights.Data.Models.Domain;

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
    public async Task<ActionResult<NotificationSettingsDto>> GetNotifications(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var prefs = await db.NotificationPreferences.FindAsync([userId], ct);
        var subscriptions = await db.NotificationSubscriptions
            .Where(s => s.UserId == userId)
            .Select(s => new NotificationCellDto(s.Kind, s.Channel))
            .ToListAsync(ct);

        return Ok(new NotificationSettingsDto(
            prefs?.TimeZone ?? "UTC",
            prefs?.Language ?? Languages.Default,
            subscriptions,
            AvailableCells()));
    }

    /// <summary>
    /// Replaces the user's notification settings. Cells that no channel can deliver today are refused
    /// (400) rather than stored silently - the grid the frontend renders comes from the same rules.
    /// </summary>
    [HttpPut("notifications")]
    public async Task<ActionResult<NotificationSettingsDto>> PutNotifications(NotificationSettingsDto request, CancellationToken ct)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"Unknown time zone '{request.TimeZone}'.");
        }

        var wanted = request.Subscriptions.Distinct().ToList();
        var locked = wanted.FirstOrDefault(c => !NotificationRules.IsAvailable(c.Kind, c.Channel));
        if (locked is not null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: $"{locked.Kind} cannot be delivered via {locked.Channel}.");
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

        prefs.TimeZone = request.TimeZone;
        prefs.Language = Languages.Normalize(request.Language);

        db.NotificationSubscriptions.RemoveRange(db.NotificationSubscriptions.Where(s => s.UserId == userId));
        db.NotificationSubscriptions.AddRange(wanted.Select(c => new NotificationSubscription
        {
            UserId = userId, Kind = c.Kind, Channel = c.Channel,
        }));
        await db.SaveChangesAsync(ct);

        return Ok(new NotificationSettingsDto(prefs.TimeZone, prefs.Language, wanted, AvailableCells()));
    }

    private static List<NotificationCellDto> AvailableCells() =>
        NotificationRules.AvailableCells().Select(c => new NotificationCellDto(c.Kind, c.Channel)).ToList();
}
