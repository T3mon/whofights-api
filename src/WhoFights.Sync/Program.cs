using WhoFights.Data;
using WhoFights.Sync.Firestore;
using WhoFights.Sync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// One-shot: fetch from Firestore, upsert into Postgres, then exit. Render's
// Cron Job scheduler is what decides when this runs (once daily) - this
// process doesn't loop or wait, it does the sync once and stops, which is
// what makes per-second Cron Job billing cheap instead of paying for an
// always-on worker that spends 99% of its time idle.
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.Configure<FirestoreOptions>(builder.Configuration.GetSection(FirestoreOptions.SectionName));
builder.Services.AddHttpClient<FirestoreEventsClient>();
builder.Services.AddScoped<EventSyncService>();
builder.Services.AddScoped<EventSyncRunner>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

try
{
    var runner = scope.ServiceProvider.GetRequiredService<EventSyncRunner>();
    await runner.RunAsync();
    return 0;
}
catch (Exception ex)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogError(ex, "Firestore event sync failed");
    return 1;
}
