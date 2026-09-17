using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WhoFights.Api.Controllers.Api;

[ApiController]
[Route("api/health")]
public class HealthController(IWebHostEnvironment env) : ControllerBase
{
    // Version bumped by hand on each change - useful for eyeballing which
    // deploy is actually live on a given environment (Render's own logs
    // show this too, but this is a one-glance check without leaving the API).
    private const string Version = "v1";

    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        environment = env.EnvironmentName,
        version = Version,
        serverTimeUtc = DateTimeOffset.UtcNow,
    });

    /// <summary>
    /// Echoes the identity carried by the caller's <c>Authorization: Bearer</c> token - a diagnostic for
    /// confirming this API is actually validating tokens minted by WhoFights.Auth, not just that it starts.
    /// </summary>
    [HttpGet("whoami")]
    [Authorize]
    public IActionResult WhoAmI()
    {
        var id = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        return Ok(new { id, email });
    }
}
