namespace WhoFights.Data.Options;

// Shared between WhoFights.Auth (mints tokens) and WhoFights.Api
// (validates them) - both must bind the exact same Issuer/Audience/
// SigningKey or a token minted by one is rejected by the other.
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string SigningKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int LifetimeDays { get; init; } = 30;
}
