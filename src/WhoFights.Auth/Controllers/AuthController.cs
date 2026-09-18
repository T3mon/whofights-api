using System.IdentityModel.Tokens.Jwt;
using System.Net;
using WhoFights.Auth.Models;
using WhoFights.Auth.Options;
using WhoFights.Auth.Services;
using WhoFights.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace WhoFights.Auth.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    JwtTokenService jwtTokenService,
    IEmailSender emailSender,
    GoogleTokenVerifier googleTokens,
    IOptions<FrontendOptions> frontendOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string GoogleLoginProvider = "Google";

    /// <summary>
    /// Exchanges a Google token (an ID token, or an access token from the OAuth popup our custom button uses) for
    /// our own JWT. Creates the user's account on their first sign-in - there's no separate registration step.
    /// </summary>
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponseDto>> SignInWithGoogle(GoogleSignInRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.IdToken) && string.IsNullOrEmpty(request.AccessToken))
        {
            return BadRequest("Either idToken or accessToken is required.");
        }

        GoogleIdentity identity;
        try
        {
            identity = !string.IsNullOrEmpty(request.AccessToken)
                ? await googleTokens.VerifyAccessTokenAsync(request.AccessToken, ct)
                : await googleTokens.VerifyIdTokenAsync(request.IdToken!);
        }
        catch (InvalidGoogleTokenException)
        {
            return Unauthorized("The Google sign-in token is invalid or expired.");
        }

        var user = await userManager.FindByLoginAsync(GoogleLoginProvider, identity.Subject);

        if (user is null)
        {
            // Google verified this email is real and owned by whoever's signing in, so it's
            // trustworthy enough to skip the usual "click the link we emailed you" step.
            user = new IdentityUser { UserName = identity.Email, Email = identity.Email, EmailConfirmed = true };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return Problem(string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }

            var addLoginResult = await userManager.AddLoginAsync(user, new UserLoginInfo(GoogleLoginProvider, identity.Subject, GoogleLoginProvider));
            if (!addLoginResult.Succeeded)
            {
                return Problem(string.Join("; ", addLoginResult.Errors.Select(e => e.Description)));
            }
        }

        var (token, expiresAt) = jwtTokenService.CreateToken(user);
        return Ok(new AuthResponseDto(token, expiresAt, user.Email!));
    }

    /// <summary>
    /// Creates an email+password account. Unlike Google sign-in, this can't trust the email is real on its own, so
    /// the account starts unconfirmed and can't sign in until the link in the confirmation email is used.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            // Deliberately not "add a password to this account" here, even if
            // existing is Google-only - that path requires being signed in as
            // that account already (see SetPassword), not just knowing its
            // email address.
            return Conflict("An account with this email already exists. Sign in instead.");
        }

        var user = new IdentityUser { UserName = request.Email, Email = request.Email, EmailConfirmed = false };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            // Bad client input (weak password, etc.) - Problem() defaults to
            // 500 without an explicit status, which would be misleading here.
            return Problem(string.Join("; ", createResult.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await SendConfirmationEmailAsync(user, ct);
        }
        catch (Exception ex)
        {
            // Don't leave a permanently-unconfirmable account behind if the email
            // never went out - undo the CreateAsync above so registration either
            // fully succeeds or fully fails, never a half-finished state.
            await userManager.DeleteAsync(user);
            logger.LogError(ex, "Failed to send confirmation email during registration for {Email}", request.Email);
            return Problem(
                "We couldn't send a confirmation email right now. Please try again shortly.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok();
    }

    /// <summary>
    /// Confirms the email address using the token from the link sent by <c>POST /auth/register</c>, and signs the
    /// user in - clicking the link already proves they control the inbox, so demanding the password again is friction.
    /// </summary>
    [HttpPost("confirm-email")]
    public async Task<ActionResult<AuthResponseDto>> ConfirmEmail(ConfirmEmailRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return BadRequest("This confirmation link is invalid.");
        }

        if (user.EmailConfirmed)
        {
            // Spent link, not a failure: people double-click, and corporate mail
            // scanners follow links before the recipient ever sees them. There's
            // no session to hand back though - see the security stamp below.
            return Conflict("This email is already confirmed. Sign in instead.");
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return BadRequest("This confirmation link is invalid or has expired.");
        }

        // Now that this link mints a session, it's a credential, and email links
        // leak - forwarded mail, browser history, screenshots. Rotating the
        // security stamp invalidates the token so the link works exactly once.
        await userManager.UpdateSecurityStampAsync(user);

        var (token, expiresAt) = jwtTokenService.CreateToken(user);
        return Ok(new AuthResponseDto(token, expiresAt, user.Email!));
    }

    /// <summary>
    /// Re-sends the confirmation email. Always returns success regardless of whether the address has an account or
    /// is already confirmed - otherwise this endpoint could be used to check who's registered.
    /// </summary>
    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null && !user.EmailConfirmed)
        {
            try
            {
                await SendConfirmationEmailAsync(user, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send confirmation email during resend for {Email}", request.Email);
                return Problem(
                    "We couldn't send a confirmation email right now. Please try again shortly.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }

        return Ok();
    }

    /// <summary>Exchanges an email+password for our own JWT, the password-based equivalent of <c>POST /auth/google</c>.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Unauthorized("Incorrect email or password.");
        }

        if (!await userManager.HasPasswordAsync(user))
        {
            // Created via Google sign-in and never added a password (see SetPassword) -
            // "incorrect password" would be misleading, there simply isn't one to check.
            return Unauthorized("This account signs in with Google. Sign in with Google, or add a password from your account settings first.");
        }

        // lockoutOnFailure: true locks the account out after repeated bad attempts
        // (IdentityUser already has the AccessFailedCount/LockoutEnd columns for
        // this) - free brute-force protection, no extra code needed here.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return Unauthorized("Too many failed attempts. Try again later.");
        }
        if (!result.Succeeded)
        {
            return Unauthorized("Incorrect email or password.");
        }

        if (!user.EmailConfirmed)
        {
            return Unauthorized("Confirm your email before signing in - check your inbox, or use /auth/resend-confirmation.");
        }

        var (token, expiresAt) = jwtTokenService.CreateToken(user);
        return Ok(new AuthResponseDto(token, expiresAt, user.Email!));
    }

    /// <summary>
    /// Adds a password to the signed-in user's own account - for someone who signed up with Google and wants
    /// email+password as a second way in (a future phone app, Telegram bot, etc). Fails if one is already set;
    /// that's a "change password" operation, not this one.
    /// </summary>
    [HttpPost("set-password")]
    [Authorize]
    public async Task<IActionResult> SetPassword(SetPasswordRequest request)
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await userManager.AddPasswordAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Same reasoning as Register: this is the client's fault (already
            // has a password, or the new one is too weak), not a server error.
            return Problem(string.Join("; ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok();
    }

    /// <summary>
    /// Permanently deletes the signed-in user's own account and everything tied to it (tracked promotions and
    /// fighters, Google login link). There is no undo.
    /// </summary>
    [HttpDelete("account")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return Ok();
    }

    /// <summary>Returns the signed-in user's identity. Requires a valid <c>Authorization: Bearer</c> token from a prior <c>POST /auth/google</c> call.</summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        return Ok(new CurrentUserDto(CurrentUserId, User.FindFirst(JwtRegisteredClaimNames.Email)?.Value!));
    }

    // Always read "who is this" from the token's own claims, never from a
    // request body field - a client can't be trusted to say which account
    // it's acting as, only the signed JWT can.
    private string CurrentUserId => User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;

    private async Task SendConfirmationEmailAsync(IdentityUser user, CancellationToken ct)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{frontendOptions.Value.BaseUrl}/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        var body = $"""
            <h1 style="margin:0 0 20px;font-size:26px;line-height:1.3;font-weight:700;color:{EmailLayout.TextBright};">Confirm your email address</h1>
            <p style="margin:0 0 28px;font-size:15px;line-height:1.6;color:{EmailLayout.TextBody};">
              To finish setting up your WhoFights account, confirm this email address. You'll be signed in automatically.
            </p>
            {EmailLayout.Button("Confirm email address", link, padding: "14px 28px")}
            """;
        const string why = "Someone used this address to create a WhoFights account. If that wasn't you, you can ignore this email - the account can't be used until it's confirmed, and this link expires.";
        var html = EmailLayout.Wrap(body, why);
        await emailSender.SendAsync(new EmailMessage(user.Email!, "Confirm your WhoFights account", html), ct);
    }
}
