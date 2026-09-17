namespace WhoFights.Auth.Models;

/// <summary>The ID token Google's Sign In With Google JS library hands the frontend after the user picks an account.</summary>
public record GoogleSignInRequest(string? IdToken, string? AccessToken);

public record AuthResponseDto(string Token, DateTimeOffset ExpiresAt, string Email);

public record CurrentUserDto(string Id, string Email);

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password);

public record ConfirmEmailRequest(string UserId, string Token);

public record ResendConfirmationRequest(string Email);

/// <summary>Adds a password to the caller's own account - for someone who signed up with Google and wants email+password as a second way in.</summary>
public record SetPasswordRequest(string Password);
