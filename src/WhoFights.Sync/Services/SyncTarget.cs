using Microsoft.Extensions.Configuration;
using Npgsql;

namespace WhoFights.Sync.Services;

// One database the feed gets written into.
public record SyncTarget(string Name, string ConnectionString)
{
    // Only for the logs - tells production and staging apart at a glance
    // without ever printing a password.
    public string Host
    {
        get
        {
            try
            {
                return new NpgsqlConnectionStringBuilder(ConnectionString).Host ?? "?";
            }
            catch (ArgumentException)
            {
                return "unparseable connection string";
            }
        }
    }

    // Every configured connection string is a target: DefaultConnection on
    // its own locally, DefaultConnection (production) plus Staging on the
    // Render cron. Keeping one more database in step is one more
    // ConnectionStrings__<Name> environment variable, no code change.
    public static IReadOnlyList<SyncTarget> FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection("ConnectionStrings").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => new SyncTarget(c.Key, c.Value!))
            .ToList();
}
