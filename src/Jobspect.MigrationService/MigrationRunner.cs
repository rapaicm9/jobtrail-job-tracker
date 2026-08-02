using System.Diagnostics;
using Jobspect.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Jobspect.MigrationService;

/// <summary>
/// Runs every module's migrations, in the order the modules registered
/// themselves, and reports whether the database is now current.
/// <para>
/// The order is deterministic but not significant: each module owns a schema and
/// a migration history table of its own, so no module's migrations can depend on
/// another's. That independence is what makes running them in one pass safe.
/// </para>
/// </summary>
internal sealed class MigrationRunner(
    IEnumerable<IModuleMigrator> migrators,
    ILogger<MigrationRunner> logger)
{
    /// <summary>
    /// Applies everything outstanding. Returns the process exit code: zero once
    /// every store is current, and non-zero the moment one is not.
    /// <para>
    /// The exit code is the whole point of this process. Both hosts wait on it, so
    /// a failure here has to stop them starting rather than be logged and
    /// forgotten - a host that starts against a half-migrated database fails later,
    /// further from the cause.
    /// </para>
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var stores = migrators.ToArray();
        MigrationLog.Starting(logger, stores.Length);

        foreach (var migrator in stores)
        {
            var timestamp = Stopwatch.GetTimestamp();

            try
            {
                await migrator.MigrateAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                // Stop at the first failure rather than pressing on: the stores are
                // independent, but a database that is only partly migrated is not a
                // state anything downstream should be allowed to start against.
                MigrationLog.Failed(logger, migrator.Store, exception);
                return 1;
            }

            MigrationLog.Migrated(logger, migrator.Store, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
        }

        MigrationLog.Finished(logger, stores.Length);
        return 0;
    }
}

/// <summary>
/// Logging goes through source-generated delegates, never the ILogger extension
/// methods: CA1848 is an error.
/// </summary>
internal static partial class MigrationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Migrating {StoreCount} module stores.")]
    public static partial void Starting(ILogger logger, int storeCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Store} is current ({ElapsedMs:F0} ms).")]
    public static partial void Migrated(ILogger logger, string store, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Store} could not be migrated; the database is left partly migrated and no host should start against it.")]
    public static partial void Failed(ILogger logger, string store, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "All {StoreCount} module stores are current.")]
    public static partial void Finished(ILogger logger, int storeCount);
}
