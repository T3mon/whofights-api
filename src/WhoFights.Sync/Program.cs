using WhoFights.Data;
using WhoFights.Email;
using WhoFights.Sync.Digest;
using WhoFights.Sync.Firestore;
using WhoFights.Sync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// One-shot: fetch from Firestore, upsert into Postgres, then exit. Render's
// Cron Job scheduler is what decides when this runs (once daily) - this
// process doesn't loop or wait, it does the sync once and stops, which is
// what makes per-second Cron Job billing cheap instead of paying for an
// always-on worker that spends 99% of its time idle.
//
// On Mondays the same run also sends the weekly email digest, straight
// after the sync so it goes out on the freshest data. Piggybacking on this
// job rather than adding a second cron service keeps one schedule, one
// container and one set of secrets to look after.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.Configure<FirestoreOptions>(builder.Configuration.GetSection(FirestoreOptions.SectionName));
builder.Services.AddHttpClient<FirestoreEventsClient>();
builder.Services.AddScoped<EventSyncService>();
builder.Services.AddScoped<EventSyncRunner>();

var digestOptions = builder.Configuration.GetSection(DigestOptions.SectionName).Get<DigestOptions>()
    ?? throw new InvalidOperationException("Digest configuration section is missing.");
builder.Services.AddSingleton(Options.Create(digestOptions));
builder.Services.AddResendEmail(builder.Configuration);
builder.Services.AddScoped<WeeklyDigestService>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

try
{
    var runner = scope.ServiceProvider.GetRequiredService<EventSyncRunner>();
    await runner.RunAsync();
}
catch (Exception ex)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogError(ex, "Firestore event sync failed");
    return 1;
}

if (digestOptions.Force || DateTime.UtcNow.DayOfWeek == DayOfWeek.Monday)
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<WeeklyDigestService>().RunAsync();
    }
    catch (Exception ex)
    {
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogError(ex, "Weekly digest failed");
        return 1;
    }
}

return 0;
