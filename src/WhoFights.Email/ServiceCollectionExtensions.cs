using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WhoFights.Email;

public static class ServiceCollectionExtensions
{
    // Fail fast at startup rather than on the first send: a missing key
    // would otherwise only surface when a real user registers (or on the
    // Monday digest run), long after the deploy looked healthy.
    public static IServiceCollection AddResendEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ResendOptions.SectionName).Get<ResendOptions>()
            ?? throw new InvalidOperationException("Resend configuration section is missing.");
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new InvalidOperationException("Resend:ApiKey / Resend:FromAddress is not configured.");
        }

        services.AddSingleton(Options.Create(options));
        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
        });
        return services;
    }
}
