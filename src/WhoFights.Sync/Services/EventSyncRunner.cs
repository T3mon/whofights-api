using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhoFights.Data;
using WhoFights.Sync.Firestore;

namespace WhoFights.Sync.Services;

// Fetches the Firestore feed once, then upserts that same feed into every
// target database in turn. One run keeps staging and production fed with
// identical data on the same schedule, for the price of one container
// start - Render bills cron jobs per second of runtime, and most of a run
// is start-up, so a second cron job would nearly double the bill.
public class EventSyncRunner(FirestoreEventsClient firestoreClient, IServiceProvider services, ILogger<EventSyncRunner> logger)
{
    /// <returns>True when every target synced, false if any of them failed.</returns>
    public async Task<bool> RunAsync(IReadOnlyList<SyncTarget> targets, CancellationToken ct = default)
    {
        var events = await firestoreClient.FetchAllEventsAsync(ct);
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        var allSucceeded = true;
        foreach (var target in targets)
        {
            // An unreachable or misconfigured database (rotated password, Neon
            // outage) must not leave the other one stale - carry on, but report
            // failure at the end so Render's failure email still goes out.
            try
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(target.ConnectionString)
                    .UseLoggerFactory(loggerFactory)
                    .Options;
                await using var db = new ApplicationDbContext(options);
                var result = await ActivatorUtilities.CreateInstance<EventSyncService>(services, db).SyncAsync(events, ct);

                logger.LogInformation(
                    "Event sync into {Target} ({Host}) complete: {Fetched} fetched, {Created} created, {Updated} updated, {Skipped} skipped",
                    target.Name, target.Host, events.Count, result.Created, result.Updated, result.Skipped);
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                logger.LogError(ex, "Event sync into {Target} ({Host}) failed", target.Name, target.Host);
            }
        }

        return allSucceeded;
    }
}
