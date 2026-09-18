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
    private const string SubSeriesSeparator = "::";

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

        return Ok(new PromotionFollowsDto(keys.Select(k => ToKey(k.Code, k.SubSeries)).ToList()));
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
            .Select(ParseKey)
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

        return Ok(new PromotionFollowsDto(wanted.Select(k => ToKey(k.Code, k.SubSeries)).ToList()));
    }

    private static string ToKey(string code, string? subSeries) =>
        subSeries is null ? code : code + SubSeriesSeparator + subSeries;

    private static (string Code, string? SubSeries) ParseKey(string key)
    {
        var at = key.IndexOf(SubSeriesSeparator, StringComparison.Ordinal);
        return at < 0 ? (key, null) : (key[..at], key[(at + SubSeriesSeparator.Length)..]);
    }
}
