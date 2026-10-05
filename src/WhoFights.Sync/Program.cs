using WhoFights.Sync.Firestore;
using WhoFights.Sync.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// One-shot: fetch events and rankings from Firestore, upsert them into
// every configured database, then exit. Render's Cron Job scheduler is what decides when this runs
// (once daily) - this process doesn't loop or wait, it does the sync once
// and stops, which is what makes per-second Cron Job billing cheap instead
// of paying for an always-on worker that spends 99% of its time idle.
//
// A single cron job serves both environments: production's connection
// string as DefaultConnection, staging's as Staging (see SyncTarget).
var builder = Host.CreateApplicationBuilder(args);

var targets = SyncTarget.FromConfiguration(builder.Configuration);
if (targets.Count == 0)
{
    throw new InvalidOperationException("No database to sync into - set ConnectionStrings__DefaultConnection.");
}

builder.Services.Configure<FirestoreOptions>(builder.Configuration.GetSection(FirestoreOptions.SectionName));
builder.Services.AddHttpClient<FirestoreClient>();
builder.Services.AddScoped<EventSyncRunner>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

try
{
    var runner = scope.ServiceProvider.GetRequiredService<EventSyncRunner>();
    return await runner.RunAsync(targets) ? 0 : 1;
}
catch (Exception ex)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogError(ex, "Firestore event sync failed");
    return 1;
}
