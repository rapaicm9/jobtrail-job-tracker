using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobspect.Modules.Notifications.Features.ArmReminders;

/// <summary>
/// Every write the arming handlers make to the reminder table, and the only place
/// SQL for it lives.
/// <para>
/// <b>Nothing here is tracked</b>, for the reason the read model next door gives:
/// the outbox dispatcher delivers a whole batch in one DI scope, so this context is
/// shared by every event in that batch, and a tracked entity left behind by one
/// event is a change the next event's save would flush. Statements carry that risk
/// not at all.
/// </para>
/// <para>
/// <b>Arming is two statements, not one, and that is forced rather than chosen.</b>
/// A row is one armed instant (ADR 0006), so re-arming a slot retires the row that
/// holds it and inserts another - which is not an upsert. The tempting single
/// statement is a data-modifying CTE, and PostgreSQL rules it out outright: the
/// sub-statements of a <c>WITH</c> "are executed concurrently with each other and
/// with the main query", so the order in which they happen is unpredictable and
/// they cannot see one another's effects. The insert would race the retire for the
/// partial unique index, intermittently and under load. So the two run in order,
/// inside one transaction.
/// </para>
/// </summary>
internal sealed class ReminderWriter(NotificationsDbContext dbContext)
{
    /// <summary>
    /// The slot a reminder occupies: the application, the round if there is one,
    /// and the kind. Null-safe on the round, because <c>interview_id</c> is null on
    /// six of the eight kinds and <c>= NULL</c> would match none of them.
    /// </summary>
    private const string SlotMatches =
        """
        application_id = @application_id
          AND interview_id IS NOT DISTINCT FROM @interview_id
          AND kind = @kind
        """;

    /// <summary>
    /// The staleness guard: this event is at least as new as anything that has
    /// already decided the slot.
    /// <para>
    /// Read over <em>every</em> state rather than the armed one alone. A redelivered
    /// "interview scheduled" arriving after the cancellation that retired it would
    /// otherwise find an empty slot and arm a reminder for a round that is not
    /// happening. <c>-infinity</c> stands in for a slot nothing has touched, so the
    /// never-armed case needs no branch of its own.
    /// </para>
    /// <para>
    /// The comparison is <c>&gt;=</c> because events published in one transaction
    /// share an instant - which is also precisely why the second guard below has to
    /// exist.
    /// </para>
    /// </summary>
    private const string EventIsNotStale =
        """
        @occurred_at >= COALESCE(
            (SELECT MAX(newest.source_recorded_at)
             FROM notifications.reminders AS newest
             WHERE newest.application_id = @application_id
               AND newest.interview_id IS NOT DISTINCT FROM @interview_id
               AND newest.kind = @kind),
            '-infinity'::timestamptz)
        """;

    /// <summary>
    /// The redelivery guard, and the one rule here that ADR 0006 does not state.
    /// <para>
    /// Because the staleness comparison is <c>&gt;=</c>, an exact redelivery of the
    /// event that armed a slot passes it: same occurrence time, same instant. Left
    /// at that, every redelivery would retire the armed row and insert an identical
    /// replacement - a new id each time, and a feed whose history says the reminder
    /// was re-armed when nothing happened. At-least-once delivery makes that the
    /// ordinary case rather than a rare one.
    /// </para>
    /// <para>
    /// So an arm is skipped outright when the slot already holds this exact armed
    /// instant, decided by this exact event. A genuine reschedule carries a
    /// different <c>due_at</c> and still replaces; a repeat changes nothing.
    /// </para>
    /// </summary>
    private const string NotAlreadyArmed =
        """
        NOT EXISTS (
            SELECT 1
            FROM notifications.reminders AS armed
            WHERE armed.application_id = @application_id
              AND armed.interview_id IS NOT DISTINCT FROM @interview_id
              AND armed.kind = @kind
              AND armed.state = 'Pending'
              AND armed.due_at = @due_at
              AND armed.source_recorded_at = @occurred_at)
        """;

    /// <summary>
    /// Makes one source's slots say what the event says: arm every instant still
    /// ahead of us, and retract every kind that no longer has one.
    /// <para>
    /// <b>The retraction half is not an optimisation.</b> The instants handed in
    /// have already been through the past-instant filter, so a kind missing from
    /// them is one whose moment has gone by - and the row still holding the previous
    /// moment has to go with it. Move a round from next week to an hour from now and
    /// the morning-before is in the past; arming only what remains would leave last
    /// week's morning armed, and it would fire for a round that has moved. The same
    /// path covers a deadline cleared to null, which produces no instants at all and
    /// so retracts every kind it owns.
    /// </para>
    /// </summary>
    public async Task ApplyAsync(
        Guid applicationId,
        UserId ownerId,
        Guid? interviewId,
        IReadOnlyList<ReminderKind> kinds,
        IReadOnlyList<ReminderInstant> instants,
        DateTimeOffset? subjectAt,
        DateOnly? subjectDate,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        foreach (var instant in instants)
        {
            await ArmAsync(
                applicationId,
                ownerId,
                instant.Kind,
                interviewId,
                instant.DueAt,
                subjectAt,
                subjectDate,
                occurredAt,
                cancellationToken);
        }

        var lapsed = kinds.Where(kind => !instants.Any(instant => instant.Kind == kind)).ToArray();

        if (lapsed.Length > 0)
        {
            await RetractAsync(applicationId, interviewId, lapsed, occurredAt, cancellationToken);
        }
    }

    /// <summary>
    /// Arms one instant: retires whatever the slot was holding, and records the new
    /// moment.
    /// <para>
    /// The two statements compose because the retire changes only <c>state</c>. The
    /// row it cancels keeps the occurrence time that armed it, so the insert's
    /// staleness guard reads the same value the retire's did and reaches the same
    /// answer - and by then no armed row is left for the slot, so the partial unique
    /// index has nothing to refuse.
    /// </para>
    /// </summary>
    public async Task ArmAsync(
        Guid applicationId,
        UserId ownerId,
        ReminderKind kind,
        Guid? interviewId,
        DateTimeOffset dueAt,
        DateTimeOffset? subjectAt,
        DateOnly? subjectDate,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        // The enriched context's retrying strategy refuses a transaction it did not
        // start, so the whole pair is handed to it to replay as a unit.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            await ExecuteAsync(
                $"""
                UPDATE notifications.reminders
                SET state = 'Cancelled'
                WHERE {SlotMatches}
                  AND state = 'Pending'
                  AND {EventIsNotStale}
                  AND {NotAlreadyArmed}
                """,
                cancellationToken,
                SlotParameters(applicationId, interviewId, kind),
                [Instant("due_at", dueAt), Instant("occurred_at", occurredAt)]);

            await ExecuteAsync(
                $"""
                INSERT INTO notifications.reminders (
                    owner_id, kind, state, due_at, application_id, interview_id,
                    subject_at, subject_date, source_recorded_at)
                SELECT
                    @owner_id, @kind, 'Pending', @due_at, @application_id, @interview_id,
                    @subject_at, @subject_date, @occurred_at
                WHERE {EventIsNotStale}
                  AND {NotAlreadyArmed}
                """,
                cancellationToken,
                SlotParameters(applicationId, interviewId, kind),
                [
                    Uuid("owner_id", ownerId.Value),
                    Instant("due_at", dueAt),
                    Instant("subject_at", subjectAt),
                    Date("subject_date", subjectDate),
                    Instant("occurred_at", occurredAt),
                ]);

            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>
    /// The staleness guard as a retraction states it, correlated against each row's
    /// own slot rather than against a slot named in parameters.
    /// <para>
    /// That correlation is what lets one statement retract several slots at once
    /// without their histories interfering: these kinds go together, but they are
    /// still separate slots, and one having been decided recently must not silence
    /// another that was not.
    /// </para>
    /// </summary>
    private const string RetractionIsNotStale =
        """
        @occurred_at >= COALESCE(
            (SELECT MAX(newest.source_recorded_at)
             FROM notifications.reminders AS newest
             WHERE newest.application_id = target.application_id
               AND newest.interview_id IS NOT DISTINCT FROM target.interview_id
               AND newest.kind = target.kind),
            '-infinity'::timestamptz)
        """;

    /// <summary>
    /// Retires what one slot's kinds still hold - the deadline was cleared, or the
    /// round is off, so the reminders for it go with it.
    /// <para>
    /// One statement, because there is nothing to insert. It stamps
    /// <c>source_recorded_at</c> with the retracting event's occurrence, which is
    /// load-bearing: a row carries the time of whatever last decided it, armed or
    /// retracted, and without the stamp the retraction would be invisible to the
    /// next staleness comparison - a redelivered arming would then find the slot
    /// still holding its original, older decision and undo the retraction.
    /// </para>
    /// <para>
    /// <b>Round-scoped, including when the round is null.</b>
    /// <c>IS NOT DISTINCT FROM</c> matches null to null, so passing no round matches
    /// exactly the kinds that have none - which is every kind but the two interview
    /// ones. That is right for a cleared deadline and wrong for a closed application:
    /// see <see cref="RetractApplicationAsync"/>.
    /// </para>
    /// </summary>
    public Task RetractAsync(
        Guid applicationId,
        Guid? interviewId,
        IReadOnlyCollection<ReminderKind> kinds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            UPDATE notifications.reminders AS target
            SET state = 'Cancelled', source_recorded_at = @occurred_at
            WHERE target.application_id = @application_id
              AND target.interview_id IS NOT DISTINCT FROM @interview_id
              AND target.kind = ANY(@kinds)
              AND target.state = 'Pending'
              AND {RetractionIsNotStale}
            """,
            cancellationToken,
            [
                Uuid("application_id", applicationId),
                Uuid("interview_id", interviewId),
                TextArray("kinds", [.. kinds.Select(kind => kind.ToString())]),
                Instant("occurred_at", occurredAt),
            ],
            []);

    /// <summary>
    /// Retires everything an application still has armed, whatever round and whatever
    /// kind - it is closed, so none of it is owed any more.
    /// <para>
    /// <b>A method of its own rather than a call to <see cref="RetractAsync"/> with no
    /// round and every kind, because that combination does not mean this.</b> The slot
    /// predicate there matches a null round to a null <c>interview_id</c>, so it would
    /// retire the deadline reminders and leave the interview ones armed - and the
    /// mistake would stay invisible until an alert fired for a round belonging to an
    /// application that was rejected weeks ago. Dropping the predicate is the only way
    /// to say "any round", and a flag inside the other statement would read as a bug
    /// long before anyone remembered why it was there.
    /// </para>
    /// <para>
    /// The kind predicate goes with it for the same reason: every kind is not a list
    /// to keep in step with the enum, it is the absence of a restriction.
    /// </para>
    /// </summary>
    public Task RetractApplicationAsync(
        Guid applicationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            UPDATE notifications.reminders AS target
            SET state = 'Cancelled', source_recorded_at = @occurred_at
            WHERE target.application_id = @application_id
              AND target.state = 'Pending'
              AND {RetractionIsNotStale}
            """,
            cancellationToken,
            [Uuid("application_id", applicationId), Instant("occurred_at", occurredAt)],
            []);

    /// <summary>
    /// The three parameters the slot predicate and both guards share. Built fresh
    /// per statement rather than shared between them: an <see cref="NpgsqlParameter"/>
    /// belongs to one command, and reusing an instance across two throws.
    /// </summary>
    private static NpgsqlParameter[] SlotParameters(Guid applicationId, Guid? interviewId, ReminderKind kind) =>
        [
            Uuid("application_id", applicationId),
            Uuid("interview_id", interviewId),
            Text("kind", kind.ToString()),
        ];

    private Task ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        NpgsqlParameter[] slot,
        NpgsqlParameter[] rest) =>
        dbContext.Database.ExecuteSqlRawAsync(sql, [.. slot, .. rest], cancellationToken);

    private static NpgsqlParameter Uuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter TextArray(string name, string[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values };

    private static NpgsqlParameter Date(string name, DateOnly? value) =>
        new(name, NpgsqlDbType.Date) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Instant(string name, DateTimeOffset? value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = (object?)value ?? DBNull.Value };
}
