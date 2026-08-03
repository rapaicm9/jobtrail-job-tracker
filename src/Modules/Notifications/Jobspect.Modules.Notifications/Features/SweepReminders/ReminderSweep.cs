using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Jobspect.Modules.Notifications.Features.SweepReminders;

/// <summary>
/// One pass over the reminders that have come due: deliver the ones still worth
/// delivering, and retire the ones it is too late to send.
/// <para>
/// <b>This is the whole firing mechanism.</b> There is no trigger per reminder - the
/// row is the reminder (ADR 0006) - so a due instant reaches its owner only because
/// this ran. It is a plain class rather than the job itself so that the clock is a
/// constructor argument: the rules below are all statements about "how late is too
/// late", and a test that could not decide what time it was would be asserting
/// against a stopwatch.
/// </para>
/// <para>
/// <b>Statements rather than tracked writes</b>, like the writer next door. Here the
/// reason is not a shared scope - the job gets one of its own per execution - but the
/// backlog: a pass after an outage walks batch after batch, and a change tracker
/// accumulating every row it has already saved is one that also has to be cleared
/// before an execution-strategy retry can replay cleanly.
/// </para>
/// </summary>
internal sealed partial class ReminderSweep(
    NotificationsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<ReminderSweep> logger)
{
    /// <summary>
    /// How late a reminder may be found and still be sent.
    /// <para>
    /// It cannot be zero: the sweep discovers every reminder slightly after its
    /// instant, so a zero tolerance would drop all of them and the module would
    /// appear to do nothing at all. Ten minutes is normal sweep jitter with headroom,
    /// and it is also what stops a worker that was down from delivering its whole
    /// backlog on the way up.
    /// </para>
    /// <para>
    /// A constant rather than a configuration knob, and deliberately unlike the
    /// outbox's poll interval. That one is a property of where the dispatcher runs;
    /// this is a product rule about what the owner is worth telling, and an
    /// environment that could reinterpret it would be able to turn the feed into
    /// exactly the noise the rule exists to prevent. The tests need no knob because
    /// they own the clock.
    /// </para>
    /// </summary>
    private static readonly TimeSpan LateTolerance = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How many reminders one claim takes. The pass keeps claiming until a batch
    /// comes back short, so this bounds how long rows stay locked rather than how
    /// much work a pass can do.
    /// </summary>
    private const int BatchSize = 100;

    /// <summary>
    /// Everything past saving, in one statement. Unbounded on purpose: after an
    /// outage the whole backlog is droppable, and sharing a batch with the delivery
    /// below would make today's reminders queue behind a week of dead ones - they
    /// sort first, being older.
    /// </summary>
    private const string DropLapsedSql =
        """
        UPDATE notifications.reminders
        SET state = 'Dropped'
        WHERE state = 'Pending'
          AND due_at < @cutoff
        """;

    /// <summary>
    /// The due reminders, claimed. <c>SKIP LOCKED</c> is not needed by one worker and
    /// is here for the same reason the outbox dispatcher has it: clustering is one
    /// configuration flag away, and this is the difference between a second worker
    /// dividing the work and two workers fighting over it.
    /// </summary>
    private static readonly string ClaimDueSql =
        $"""
        SELECT id AS "Value"
        FROM notifications.reminders
        WHERE state = 'Pending'
          AND due_at <= @swept_at
        ORDER BY due_at, id
        LIMIT {BatchSize}
        FOR UPDATE SKIP LOCKED
        """;

    /// <summary>
    /// The delivery itself - in-app delivery is this row and nothing else, so there
    /// is no external call here to fail.
    /// <para>
    /// <c>ON CONFLICT DO NOTHING</c> is the guard the key exists for: a second
    /// attempt at the same reminder is refused by the database rather than reaching
    /// the owner twice. Absorbed rather than thrown, because a batch that rolled back
    /// on it would claim the same row again on the next pass and fail the same way,
    /// forever.
    /// </para>
    /// </summary>
    private const string RecordDeliverySql =
        """
        INSERT INTO notifications.reminder_deliveries (reminder_id, channel, delivered_at)
        SELECT claimed.id, @channel, @delivered_at
        FROM unnest(@ids) AS claimed(id)
        ON CONFLICT (reminder_id, channel) DO NOTHING
        """;

    /// <summary>
    /// Dealt with - stop sweeping it. Separate from the delivery above because the
    /// two say different things: this is the decision, that is the act, and only the
    /// act carries when the owner was actually told.
    /// </summary>
    private const string MarkSentSql =
        """
        UPDATE notifications.reminders
        SET state = 'Sent'
        WHERE id = ANY(@ids)
        """;

    /// <summary>
    /// Drops what is past saving, then delivers what is left.
    /// <para>
    /// <b>One reading of the clock decides the whole pass</b>, and that is what makes
    /// the two steps compose. The drop takes everything before
    /// <c>now - <see cref="LateTolerance"/></c> and the delivery everything up to
    /// <c>now</c>, so every row the second step can claim is inside the tolerance by
    /// construction - there is no third case and no per-row branch deciding which of
    /// the two a reminder belongs to. Nothing can arrive in the gap between them
    /// either: arming never records an instant that has already passed, so no
    /// <c>Pending</c> row older than the cutoff can appear mid-pass.
    /// </para>
    /// <para>
    /// Reminders that come due <em>during</em> a long pass are left for the next one,
    /// for the same reason: they are later than the reading this pass is judged by.
    /// </para>
    /// </summary>
    public async Task<SweepOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        var sweptAt = timeProvider.GetUtcNow();

        var dropped = await DropLapsedAsync(sweptAt - LateTolerance, cancellationToken);
        var delivered = await DeliverDueAsync(sweptAt, cancellationToken);

        if (dropped > 0)
        {
            DroppedAsTooLate(dropped, LateTolerance);
        }

        if (delivered > 0)
        {
            DeliveredDue(delivered);
        }

        return new SweepOutcome(delivered, dropped);
    }

    private Task<int> DropLapsedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            DropLapsedSql, [Instant("cutoff", cutoff)], cancellationToken);

    /// <summary>Keeps claiming until a batch comes back short, so a backlog drains in one pass.</summary>
    private async Task<int> DeliverDueAsync(DateTimeOffset sweptAt, CancellationToken cancellationToken)
    {
        var delivered = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var claimed = await DeliverBatchAsync(sweptAt, cancellationToken);
            delivered += claimed;

            if (claimed < BatchSize)
            {
                break;
            }
        }

        return delivered;
    }

    private async Task<int> DeliverBatchAsync(DateTimeOffset sweptAt, CancellationToken cancellationToken)
    {
        // The enriched context's retrying strategy refuses a transaction it did not
        // start, so claim, record and mark are handed to it to replay as a unit.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Nothing is composed onto this query: appending LINQ would make EF wrap
            // it in a subquery, where FOR UPDATE is not allowed. The order, the limit
            // and the lock all live in the statement itself.
            var claimed = await dbContext.Database
                .SqlQueryRaw<Guid>(ClaimDueSql, Instant("swept_at", sweptAt))
                .ToListAsync(cancellationToken);

            if (claimed.Count > 0)
            {
                var ids = claimed.ToArray();

                await dbContext.Database.ExecuteSqlRawAsync(
                    RecordDeliverySql,
                    [
                        UuidArray("ids", ids),
                        Text("channel", nameof(DeliveryChannel.InApp)),

                        // The delivery time is the sweep's own reading, not the
                        // database's - which is why the column carries no default.
                        Instant("delivered_at", sweptAt),
                    ],
                    cancellationToken);

                await dbContext.Database.ExecuteSqlRawAsync(
                    MarkSentSql, [UuidArray("ids", ids)], cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return claimed.Count;
        });
    }

    private static NpgsqlParameter Text(string name, string value) =>
        new(name, NpgsqlDbType.Text) { Value = value };

    /// <summary>
    /// Built fresh per statement rather than shared between them: an
    /// <see cref="NpgsqlParameter"/> belongs to one command, and reusing an instance
    /// across two throws.
    /// </summary>
    private static NpgsqlParameter UuidArray(string name, Guid[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = values };

    private static NpgsqlParameter Instant(string name, DateTimeOffset value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = value };

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Delivered {Delivered} due reminders.")]
    private partial void DeliveredDue(int delivered);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Dropped {Dropped} reminders found more than {Tolerance} past their instant; "
            + "they were owed and were never sent.")]
    private partial void DroppedAsTooLate(int dropped, TimeSpan tolerance);
}
