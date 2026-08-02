using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobspect.Modules.Notifications.Features.TrackApplications;

/// <summary>
/// The two writes that keep this module's record of the applications it watches
/// for silence.
/// <para>
/// Both are one <c>INSERT … ON CONFLICT (application_id) DO UPDATE</c>, which is
/// what makes them idempotent by construction: the row is created if the
/// application is new here and merged into if it is not, so a redelivery re-runs
/// the same statement to the same end. There is no read-then-write, and so no
/// window between them - and nothing is tracked, for the reason the reminder writer
/// gives.
/// </para>
/// <para>
/// Unlike the reminder slots, neither of these needs a transaction. Each is a
/// single statement whose whole effect is one row, and the ordering rule they need
/// lives inside the statement.
/// </para>
/// </summary>
internal sealed class TrackedApplicationWriter(NotificationsDbContext dbContext)
{
    /// <summary>
    /// A new application to watch: who owns it, and the date they say they applied.
    /// <para>
    /// Only this event carries that date and it is published once, so a redelivery
    /// rewrites identical values and no guard is needed. The stage is deliberately
    /// not written here - this event does not carry one, and asserting where an
    /// application starts would be this module claiming knowledge that belongs to
    /// the one that owns the pipeline.
    /// </para>
    /// </summary>
    public Task SubmittedAsync(
        Guid applicationId,
        UserId ownerId,
        DateOnly appliedDate,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO notifications.tracked_applications (application_id, owner_id, applied_date)
            VALUES (@application_id, @owner_id, @applied_date)
            ON CONFLICT (application_id) DO UPDATE SET
                owner_id     = excluded.owner_id,
                applied_date = excluded.applied_date
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Date("applied_date", appliedDate));

    /// <summary>
    /// Where the application sits now, as the text the event carried.
    /// <para>
    /// Guarded, and by the same shape the read model next door uses: the assignment
    /// carries the <c>CASE</c> rather than the <c>DO UPDATE</c> carrying a
    /// <c>WHERE</c>, so a stale event cannot skip the row's creation. Without the
    /// guard an out-of-order delivery would put an answered application back into
    /// the pool of ones still waiting for an answer.
    /// </para>
    /// <para>
    /// The row may not exist yet: only the submission carries the applied date, so
    /// an application whose stage change is delivered first appears here before that
    /// date is known. Refusing the row would drop the stage change, and this table
    /// would then believe an application was still waiting for an answer it had
    /// already had - which is why the column is nullable.
    /// </para>
    /// </summary>
    public Task StageChangedAsync(
        Guid applicationId,
        UserId ownerId,
        string stage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO notifications.tracked_applications AS t (
                application_id, owner_id, stage, stage_recorded_at)
            VALUES (@application_id, @owner_id, @stage, @occurred_at)
            ON CONFLICT (application_id) DO UPDATE SET
                owner_id = excluded.owner_id,
                stage    = CASE WHEN @occurred_at >= COALESCE(t.stage_recorded_at, '-infinity'::timestamptz)
                                THEN excluded.stage ELSE t.stage END,
                stage_recorded_at = GREATEST(t.stage_recorded_at, excluded.stage_recorded_at)
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Text("stage", stage),
            Instant("occurred_at", occurredAt));

    private Task ExecuteAsync(string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters) =>
        dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

    private static NpgsqlParameter Uuid(string name, Guid value) =>
        new(name, NpgsqlDbType.Uuid) { Value = value };

    private static NpgsqlParameter Text(string name, string value) =>
        new(name, NpgsqlDbType.Text) { Value = value };

    private static NpgsqlParameter Date(string name, DateOnly value) =>
        new(name, NpgsqlDbType.Date) { Value = value };

    private static NpgsqlParameter Instant(string name, DateTimeOffset value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = value };
}
