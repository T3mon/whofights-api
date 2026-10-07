using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using WhoFights.Data;
using WhoFights.Sync.Firestore;

namespace WhoFights.Sync.Services;

// Fetches the scraper's events and rankings from Firestore once, then writes
// both into every target database in turn. One run keeps staging and
// production fed with identical data on the same schedule, for the price of
// one container start - Render bills cron jobs per second of runtime, and
// most of a run is start-up, so a second cron job would nearly double the bill.
public class EventSyncRunner(FirestoreClient firestoreClient, IServiceProvider services, ILogger<EventSyncRunner> logger)
{
    /// <returns>True when everything synced into every target, false if anything failed.</returns>
    public async Task<bool> RunAsync(IReadOnlyList<SyncTarget> targets, CancellationToken ct = default)
    {
        var events = await firestoreClient.FetchAllEventsAsync(ct);
        var allSucceeded = true;

        // Rankings are a separate collection; failing to read them must not
        // stop the events, which are what the calendar can't do without.
        List<RankingListDto>? rankings = null;
        try
        {
            rankings = await firestoreClient.FetchAllRankingsAsync(ct);
        }
        catch (Exception ex)
        {
            allSucceeded = false;
            logger.LogError(ex, "Fetching rankings from Firestore failed; syncing events only");
        }

        foreach (var target in targets)
        {
            // An unreachable or misconfigured database (rotated password, Neon
            // outage) must not leave the other one stale - carry on, but report
            // failure at the end so Render's failure email still goes out.
            try
            {
                await using var db = CreateDbContext(target);
                var result = await ActivatorUtilities.CreateInstance<EventSyncService>(services, db).SyncAsync(events, ct);
                logger.LogInformation(
                    "Event sync into {Target} ({Host}) complete: {Fetched} fetched, {Created} created, {Updated} updated, {Skipped} skipped",
                    target.Name, target.Host, events.Count, result.Created, result.Updated, result.Skipped);
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                logger.LogError(ex, "Event sync into {Target} ({Host}) failed", target.Name, target.Host);
                // Still unreachable after the retries: the rankings would only
                // wait out the same retries again, on billed time.
                if (ex is RetryLimitExceededException) continue;
            }

            if (rankings is null) continue;

            // A fresh context, so nothing a failed event sync left half-done
            // can be saved along with the rankings. Runs after the events so
            // fighters new on this run's cards can already be linked.
            try
            {
                await using var db = CreateDbContext(target);
                var result = await ActivatorUtilities.CreateInstance<RankingSyncService>(services, db).SyncAsync(rankings, ct);
                logger.LogInformation(
                    "Ranking sync into {Target} ({Host}) complete: {Saved} lists saved, {Rejected} kept from before (invalid), {Removed} retired, {Linked} of {Entries} names linked to our fighters",
                    target.Name, target.Host, result.Saved, result.Rejected, result.Removed, result.Linked, result.Entries);
                if (result.Rejected > 0) allSucceeded = false;
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                logger.LogError(ex, "Ranking sync into {Target} ({Host}) failed", target.Name, target.Host);
            }
        }

        return allSucceeded;
    }

    // Neon suspends a database that's been idle, and one slow to wake (or a
    // network blip) timed out a whole staging sync once. So each query and
    // save is retried on a transient error - a few times only, since Render
    // bills the cron per second and a database that's really down won't come
    // back within the run anyway.
    private ApplicationDbContext CreateDbContext(SyncTarget target) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(target.ConnectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
            .UseLoggerFactory(services.GetRequiredService<ILoggerFactory>())
            .Options);
}
