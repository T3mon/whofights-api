namespace WhoFights.Email;

public class ResendOptions
{
    public const string SectionName = "Resend";

    public required string ApiKey { get; init; }
    public required string FromAddress { get; init; }
    public required string FromName { get; init; }
}
