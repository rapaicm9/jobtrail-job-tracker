using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// Delivers the events one module has recorded, for the life of the host. Each
/// tick claims a batch of owed rows, runs their handlers, and marks processed only
/// those that succeeded - so an event survives a crash at any point up to the
/// moment its handlers have all run. Delivery is therefore at-least-once, and
/// handlers must be idempotent.
/// <para>
/// Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so two dispatchers - two
/// API instances, or an API and a worker - divide the work instead of both
/// delivering everything.
/// </para>
/// </summary>
internal sealed partial class OutboxDispatcher<TDbContext>(
    OutboxEventRegistry registry,
    OutboxOptions options,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private string? _claimSql;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval, timeProvider);
        var nextPrune = timeProvider.GetUtcNow() + PruneInterval;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await DrainAsync(stoppingToken);

                    if (timeProvider.GetUtcNow() >= nextPrune)
                    {
                        await PruneAsync(stoppingToken);
                        nextPrune = timeProvider.GetUtcNow() + PruneInterval;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // A database blip must not end the loop. A dead dispatcher
                    // stops every future event silently, which is far worse than
                    // one lost tick - the rows are still there to claim next time.
                    PollFailed(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown, not a fault.
        }
    }

    /// <summary>Keeps claiming until a batch comes back short, so a burst drains in one tick.</summary>
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await ProcessBatchAsync(cancellationToken) < options.BatchSize)
            {
                return;
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        // The retrying execution strategy refuses a transaction it did not start,
        // so the whole claim-deliver-commit block is handed to it to replay.
        using var scope = scopeFactory.CreateScope();
        var strategy = scope.ServiceProvider.GetRequiredService<TDbContext>().Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // A fresh scope per attempt: replaying against the change tracker of a
            // failed attempt would carry its half-applied state into the retry.
            using var attempt = scopeFactory.CreateScope();
            var dbContext = attempt.ServiceProvider.GetRequiredService<TDbContext>();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Nothing is composed onto this query: appending LINQ would make EF
            // wrap it in a subquery, where FOR UPDATE is not allowed. The order,
            // the limit and the lock all live in the statement itself.
            var claimed = await dbContext.Set<OutboxMessage>()
                .FromSqlRaw(ClaimSql(dbContext))
                .ToListAsync(cancellationToken);

            foreach (var message in claimed)
            {
                await DeliverAsync(message, attempt.ServiceProvider, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return claimed.Count;
        });
    }

    private async Task DeliverAsync(
        OutboxMessage message, IServiceProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            await registry.DeliverAsync(message.EventType, message.Payload, provider, cancellationToken);
            message.MarkProcessed(timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The row stays unprocessed, which is the whole point: an event whose
            // handlers did not run is still owed.
            var attempt = message.Attempts + 1;
            message.RecordFailure(Describe(exception), timeProvider.GetUtcNow() + RetryDelay(attempt));
            DeliveryFailed(message.EventType, message.Id, attempt, exception);
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - options.Retention;

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await dbContext.Set<OutboxMessage>()
            .Where(message => message.ProcessedAt != null && message.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private TimeSpan RetryDelay(int attempt)
    {
        // Doubling, capped: a broken handler is retried steadily rather than
        // hammered every tick, and the shift is bounded so it cannot overflow.
        var doublings = Math.Min(attempt - 1, 16);
        var delay = options.BaseRetryDelay * Math.Pow(2, doublings);

        return delay < options.MaxRetryDelay ? delay : options.MaxRetryDelay;
    }

    private static string Describe(Exception exception)
    {
        var described = $"{exception.GetType().Name}: {exception.Message}";
        return described.Length <= OutboxMessage.MaxErrorLength
            ? described
            : described[..OutboxMessage.MaxErrorLength];
    }

    private string ClaimSql(DbContext dbContext) => _claimSql ??= BuildClaimSql(dbContext);

    /// <summary>
    /// Builds the claim statement from the model rather than from a hard-coded
    /// name: the table belongs to the publishing module, which chooses its schema
    /// and (through the naming convention) its column names.
    /// </summary>
    private string BuildClaimSql(DbContext dbContext)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException(
                $"{typeof(TDbContext).Name} does not map {nameof(OutboxMessage)}, "
                + "so the outbox dispatcher has nothing to read.");

        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"{nameof(OutboxMessage)} is not mapped to a table.");

        var qualified = table.Schema is { } schema ? $"{Quote(schema)}.{Quote(table.Name)}" : Quote(table.Name);
        var processedAt = Column(nameof(OutboxMessage.ProcessedAt));
        var nextAttemptAt = Column(nameof(OutboxMessage.NextAttemptAt));

        return $"""
            SELECT * FROM {qualified}
            WHERE {processedAt} IS NULL
              AND {Column(nameof(OutboxMessage.Attempts))} < {options.MaxAttempts}
              AND ({nextAttemptAt} IS NULL OR {nextAttemptAt} <= now())
            ORDER BY {Column(nameof(OutboxMessage.OccurredAt))}, {Column(nameof(OutboxMessage.Id))}
            LIMIT {options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """;

        string Column(string propertyName) =>
            Quote(entityType.GetProperty(propertyName).GetColumnName(table)
                ?? throw new InvalidOperationException(
                    $"{nameof(OutboxMessage)}.{propertyName} is not mapped to a column on {table.Name}."));
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The outbox poll failed; the owed events remain and will be claimed again next tick.")]
    private partial void PollFailed(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Delivery of outbox event {EventType} ({OutboxMessageId}) failed on attempt {Attempt}; "
            + "it stays owed and will be retried.")]
    private partial void DeliveryFailed(string eventType, Guid outboxMessageId, int attempt, Exception exception);
}
