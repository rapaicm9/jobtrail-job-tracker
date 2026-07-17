namespace JobTrail.Worker;

/// <summary>
/// Placeholder background service so the host has something to run and the
/// skeleton can be orchestrated end to end. The real work - Redis Streams
/// consumers for push delivery and the Hangfire server for scheduled reminder
/// evaluation - replaces this.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WorkerLog.Started(logger);
        stoppingToken.Register(() => WorkerLog.Stopping(logger));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Logging goes through source-generated delegates, never the ILogger
/// extension methods: CA1848 is an error, so this is the pattern to copy.
/// </summary>
internal static partial class WorkerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Worker started.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker stopping.")]
    public static partial void Stopping(ILogger logger);
}
