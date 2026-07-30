using JobTrail.Modules.Analytics.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// Every write the projections make to the base row, and the only place SQL for
/// it lives.
/// <para>
/// Each is one <c>INSERT … ON CONFLICT (application_id) DO UPDATE</c>, which is
/// what makes a projection idempotent by construction: the row is created if the
/// application is new here and merged into if it is not, so a redelivery re-runs
/// the same statement to the same end. There is no read-then-write, and so no
/// window between them.
/// </para>
/// <para>
/// <b>Nothing here is tracked.</b> The outbox dispatcher delivers a whole batch in
/// one DI scope, so this context is shared by every event in that batch - and a
/// tracked entity left behind by one event is a change the next event's save
/// would flush. Statements carry that risk not at all, which is a large part of
/// why they are statements. Adding a tracked write to a projection would bring it
/// back.
/// </para>
/// <para>
/// <b>Columns state their own rules, and this is the part worth reading twice.</b>
/// A <c>WHERE</c> on the <c>DO UPDATE</c> would have been the obvious way to apply
/// the ordering guard, and it is wrong: it gates the whole update, so a stale
/// event would skip the monotone columns too - and those are precisely the ones
/// that are meant to be immune to ordering. So the guard sits on each latest-wins
/// assignment as a <c>CASE</c>, and the monotone assignments sit outside it.
/// </para>
/// </summary>
internal sealed class ApplicationFactsWriter(AnalyticsDbContext dbContext)
{
    /// <summary>
    /// The guard on a latest-wins column: apply only if this event is at least as
    /// new as whatever last wrote the group.
    /// <para>
    /// <c>&gt;=</c> rather than <c>&gt;</c>, and the tie is the reason. A move and
    /// the terminal or reopening it amounts to are recorded in one transaction and
    /// carry one instant, so a strict comparison would let whichever arrived first
    /// silently discard the other. They write disjoint columns, so applying both on
    /// a tie is safe; dropping one is not.
    /// </para>
    /// <para>
    /// <c>-infinity</c> stands in for a row that has never been written, which is
    /// earlier than every real timestamp - so the never-written case needs no
    /// branch of its own.
    /// </para>
    /// </summary>
    private const string StageIsNewer =
        "@occurred_at >= COALESCE(f.stage_recorded_at, '-infinity'::timestamptz)";

    private const string CampaignIsNewer =
        "@occurred_at >= COALESCE(f.campaign_recorded_at, '-infinity'::timestamptz)";

    /// <summary>
    /// What a user first told us about an application: the dimensions no other
    /// event carries, the campaign it opened in, and the stage it starts at.
    /// <para>
    /// The dimensions are written unguarded - only this event carries them and it
    /// is published once, so a redelivery rewrites identical values. The campaign
    /// and the stage are shared with later events and are guarded.
    /// </para>
    /// </summary>
    public Task SubmissionAsync(
        Guid applicationId,
        UserId ownerId,
        Guid campaignId,
        Guid? companyId,
        DateOnly appliedDate,
        string? source,
        string? workMode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            INSERT INTO analytics.application_facts AS f (
                application_id, owner_id, campaign_id, campaign_recorded_at, company_id,
                applied_date, source, work_mode, stage, stage_entered_at, stage_recorded_at)
            VALUES (
                @application_id, @owner_id, @campaign_id, @occurred_at, @company_id,
                @applied_date, @source, @work_mode, @stage, @occurred_at, @occurred_at)
            ON CONFLICT (application_id) DO UPDATE SET
                company_id   = excluded.company_id,
                applied_date = excluded.applied_date,
                source       = excluded.source,
                work_mode    = excluded.work_mode,
                campaign_id  = CASE WHEN {CampaignIsNewer}
                                    THEN excluded.campaign_id ELSE f.campaign_id END,
                campaign_recorded_at = GREATEST(f.campaign_recorded_at, excluded.campaign_recorded_at),
                stage            = CASE WHEN {StageIsNewer} THEN excluded.stage ELSE f.stage END,
                stage_entered_at = CASE WHEN {StageIsNewer}
                                        THEN excluded.stage_entered_at ELSE f.stage_entered_at END,
                stage_recorded_at = GREATEST(f.stage_recorded_at, excluded.stage_recorded_at)
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Uuid("campaign_id", campaignId),
            Uuid("company_id", companyId),
            Date("applied_date", appliedDate),
            Text("source", source),
            Text("work_mode", workMode),
            Text("stage", PipelineStages.Applied),
            Instant("occurred_at", occurredAt));

    /// <summary>
    /// Where the application sits now, and the funnel timestamps the move implies.
    /// <para>
    /// The reached-at values are the caller's read of the move; they are merged
    /// with <c>LEAST</c>, so the earliest arrival wins regardless of what order the
    /// moves are delivered in, and re-applying one changes nothing. Nothing is
    /// inferred from the move's <em>from</em> end: a move out of Screening proves
    /// the application was there but not when it arrived, and a guessed timestamp
    /// would be invented data.
    /// </para>
    /// </summary>
    public Task StageAsync(
        Guid applicationId,
        UserId ownerId,
        string stage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var reached = (DateTimeOffset?)occurredAt;

        return ExecuteAsync(
            $"""
            INSERT INTO analytics.application_facts AS f (
                application_id, owner_id, stage, stage_entered_at, stage_recorded_at,
                first_response_at, reached_screening_at, reached_interview_at, reached_offer_at)
            VALUES (
                @application_id, @owner_id, @stage, @occurred_at, @occurred_at,
                @first_response_at, @reached_screening_at, @reached_interview_at, @reached_offer_at)
            ON CONFLICT (application_id) DO UPDATE SET
                stage            = CASE WHEN {StageIsNewer} THEN excluded.stage ELSE f.stage END,
                stage_entered_at = CASE WHEN {StageIsNewer}
                                        THEN excluded.stage_entered_at ELSE f.stage_entered_at END,
                stage_recorded_at = GREATEST(f.stage_recorded_at, excluded.stage_recorded_at),
                first_response_at    = LEAST(f.first_response_at,    excluded.first_response_at),
                reached_screening_at = LEAST(f.reached_screening_at, excluded.reached_screening_at),
                reached_interview_at = LEAST(f.reached_interview_at, excluded.reached_interview_at),
                reached_offer_at     = LEAST(f.reached_offer_at,     excluded.reached_offer_at)
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Text("stage", stage),
            Instant("occurred_at", occurredAt),
            Instant("first_response_at", PipelineStages.IsResponse(stage) ? reached : null),
            Instant("reached_screening_at", stage == PipelineStages.Screening ? reached : null),
            Instant("reached_interview_at", stage == PipelineStages.Interview ? reached : null),
            Instant("reached_offer_at", stage == PipelineStages.Offer ? reached : null));
    }

    /// <summary>
    /// The outcome an application ended on, or its removal when a closed
    /// application is reopened - one statement, because setting and clearing are
    /// the same write with a different value, and they compete for the same two
    /// columns.
    /// <para>
    /// Which stages are terminal is the Applications module's knowledge, not ours,
    /// which is why this is driven by the events that say so outright rather than
    /// inferred from a stage name.
    /// </para>
    /// </summary>
    public Task OutcomeAsync(
        Guid applicationId,
        UserId ownerId,
        string? outcome,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            INSERT INTO analytics.application_facts AS f (
                application_id, owner_id, outcome, closed_at, stage_recorded_at)
            VALUES (@application_id, @owner_id, @outcome, @closed_at, @occurred_at)
            ON CONFLICT (application_id) DO UPDATE SET
                outcome   = CASE WHEN {StageIsNewer} THEN excluded.outcome   ELSE f.outcome   END,
                closed_at = CASE WHEN {StageIsNewer} THEN excluded.closed_at ELSE f.closed_at END,
                stage_recorded_at = GREATEST(f.stage_recorded_at, excluded.stage_recorded_at)
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Text("outcome", outcome),
            Instant("closed_at", outcome is null ? null : occurredAt),
            Instant("occurred_at", occurredAt));

    /// <summary>
    /// The campaign an application was moved into. Its own group, and its own
    /// watermark: a move competes only with the opening attribution and with other
    /// moves, never with a stage change that happens to be newer.
    /// </summary>
    public Task CampaignAsync(
        Guid applicationId,
        UserId ownerId,
        Guid campaignId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            INSERT INTO analytics.application_facts AS f (
                application_id, owner_id, campaign_id, campaign_recorded_at)
            VALUES (@application_id, @owner_id, @campaign_id, @occurred_at)
            ON CONFLICT (application_id) DO UPDATE SET
                campaign_id = CASE WHEN {CampaignIsNewer}
                                   THEN excluded.campaign_id ELSE f.campaign_id END,
                campaign_recorded_at = GREATEST(f.campaign_recorded_at, excluded.campaign_recorded_at)
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Uuid("campaign_id", campaignId),
            Instant("occurred_at", occurredAt));

    /// <summary>
    /// When the first interview round went on the calendar. Monotone, so a round
    /// moved to a later time never pushes this forward, and no watermark is
    /// needed.
    /// </summary>
    public Task InterviewScheduledAsync(
        Guid applicationId,
        UserId ownerId,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            INSERT INTO analytics.application_facts AS f (
                application_id, owner_id, first_interview_scheduled_at)
            VALUES (@application_id, @owner_id, @scheduled_at)
            ON CONFLICT (application_id) DO UPDATE SET
                first_interview_scheduled_at =
                    LEAST(f.first_interview_scheduled_at, excluded.first_interview_scheduled_at)
            """,
            cancellationToken,
            Uuid("application_id", applicationId),
            Uuid("owner_id", ownerId.Value),
            Instant("scheduled_at", scheduledAt));

    /// <summary>
    /// Runs one statement. No transaction and no execution strategy, both by
    /// design: a single idempotent statement needs neither, and the outbox already
    /// retries the whole event, which is the layer worth retrying at.
    /// </summary>
    private Task ExecuteAsync(string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters) =>
        dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

    private static NpgsqlParameter Uuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Date(string name, DateOnly? value) =>
        new(name, NpgsqlDbType.Date) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Instant(string name, DateTimeOffset? value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = (object?)value ?? DBNull.Value };
}
