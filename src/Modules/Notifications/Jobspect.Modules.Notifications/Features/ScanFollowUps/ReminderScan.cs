using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Jobspect.Modules.Notifications.Features.ScanFollowUps;

/// <summary>
/// One pass over the applications this module is watching: which of them have gone
/// unanswered for as long as their owner's rule says, and a follow-up raised for each.
/// <para>
/// <b>The only reminder in this module raised from an absence.</b> Everything else is
/// armed from a date the owner typed - a round they booked, a deadline they entered -
/// and arrives before the moment it is about. A silence has no such moment and
/// announces itself to nobody, which is why it takes a schedule to notice and why this
/// exists at all.
/// </para>
/// <para>
/// A plain class with the job as an adapter over it, exactly as the sweep is split and
/// for the same reason: every rule here is a statement about how long is too long, and
/// a test that could not decide what day it was would be asserting against the
/// calendar.
/// </para>
/// <para>
/// <b>Its work drains itself.</b> An application that has been nudged is excluded from
/// every later pass, so the large pass is the first one after an account turns the
/// automation on - the one holding all the applications that were already silent - and
/// the steady state is whatever crossed the threshold in the last hour.
/// </para>
/// </summary>
internal sealed partial class ReminderScan(
    NotificationsDbContext dbContext,
    IUserProfileQuery profiles,
    TimeProvider timeProvider,
    ILogger<ReminderScan> logger)
{
    /// <summary>
    /// The one stage that means "still waiting", as the text the event carried. The
    /// pipeline belongs to the Applications module and this is the single word of its
    /// vocabulary this module has to know - it cannot ask which stages are still
    /// open, and a stage recorded here that this does not recognise is simply one it
    /// does not act on.
    /// <para>
    /// A null stage reads the same way, and is the ordinary case: most applications
    /// never move, and only <c>ApplicationSubmitted</c> creates the row - which
    /// carries no stage, because where an application starts is not this module's to
    /// assert.
    /// </para>
    /// </summary>
    private const string Waiting = "Applied";

    /// <summary>
    /// Raises what is owed, and answers with how much - the number the log reports
    /// and the only thing a caller could want from a pass that otherwise leaves its
    /// results in a table.
    /// </summary>
    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var scannedAt = timeProvider.GetUtcNow();

        var candidates = await FindCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var due = await SelectDueAsync(candidates, scannedAt, cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var raised = await RaiseAsync(due, scannedAt, cancellationToken);

        if (raised > 0)
        {
            RaisedFollowUps(raised);
        }

        return raised;
    }

    /// <summary>
    /// Every application that could be nudged, before the calendar has its say.
    /// <para>
    /// <b>The exclusion is the whole trap in this feature, and it is
    /// "in any state".</b> The unique index that stops a slot being armed twice is
    /// partial on <c>Pending</c>, deliberately - a delivered reminder is history and
    /// must not stop the same slot being armed again when a round moves. A follow-up
    /// is the one kind nothing ever re-arms, so the moment one is delivered its slot
    /// falls free, and an exclusion that only looked at armed rows would raise the
    /// same nudge every hour for the rest of the application's life.
    /// </para>
    /// <para>
    /// The consequence is accepted rather than worked around: a move retracts the
    /// follow-up, so a reopened application's sits <c>Cancelled</c> and is excluded
    /// from ever getting another. The alternative needs the date an application
    /// entered its stage, which this module does not record - and without it, an
    /// application reopened a year after it was filed is instantly old enough to
    /// nudge about.
    /// </para>
    /// <para>
    /// The date arithmetic is deliberately absent here. "N days ago" is a question
    /// about the owner's calendar, and answering it in SQL would mean answering it in
    /// UTC - a day early for everyone east of it. What SQL is asked for is what SQL
    /// can index: the accounts with a rule, the applications still waiting, and the
    /// ones never nudged.
    /// </para>
    /// </summary>
    private Task<List<FollowUpCandidate>> FindCandidatesAsync(CancellationToken cancellationToken) =>
        (from tracked in dbContext.TrackedApplications.AsNoTracking()
         join rule in dbContext.ReminderRules.AsNoTracking() on tracked.OwnerId equals rule.OwnerId
         where tracked.AppliedDate != null
             && (tracked.Stage == null || tracked.Stage == Waiting)
             && !dbContext.Reminders.Any(existing =>
                 existing.ApplicationId == tracked.ApplicationId
                 && existing.Kind == ReminderKind.FollowUp)
         select new FollowUpCandidate(
             tracked.ApplicationId,
             tracked.OwnerId,
             tracked.AppliedDate!.Value,
             rule.Id,
             rule.DaysAfterApplied))
        .ToListAsync(cancellationToken);

    /// <summary>
    /// The ones that really have waited long enough, each with the moment it will be
    /// raised for.
    /// <para>
    /// The owner's zone is read once per account rather than once per application:
    /// every application in the group shares it, and it is a call across a module
    /// boundary.
    /// </para>
    /// </summary>
    private async Task<List<(FollowUpCandidate Candidate, DateTimeOffset DueAt)>> SelectDueAsync(
        List<FollowUpCandidate> candidates, DateTimeOffset scannedAt, CancellationToken cancellationToken)
    {
        var due = new List<(FollowUpCandidate, DateTimeOffset)>();

        foreach (var owned in candidates.GroupBy(candidate => candidate.OwnerId))
        {
            var zone = await profiles.GetTimezoneAsync(owned.Key, cancellationToken);
            var today = LocalDate.TodayIn(scannedAt, zone);

            // One instant for the whole account, because they all fire at the same
            // next morning. Computed once here rather than per row so a group cannot
            // straddle a boundary the loop crossed while it ran.
            var dueAt = ReminderInstants.NextMorningAfter(scannedAt, zone);

            due.AddRange(owned
                .Where(candidate => candidate.AppliedDate.AddDays(candidate.DaysAfterApplied) <= today)
                .Select(candidate => (candidate, dueAt)));
        }

        return due;
    }

    /// <summary>
    /// The follow-ups themselves, in one statement.
    /// <para>
    /// <b>A plain insert, not the arming path the events use.</b> That one retires
    /// whatever the slot holds before inserting, and carries a staleness guard and a
    /// redelivery guard - all three of which exist because an event may arrive twice
    /// and out of order. Nothing here arrives: the slot is empty by construction,
    /// since a candidate with any follow-up at all was excluded before this ran.
    /// Reusing the arming path would mean carrying machinery whose reason does not
    /// apply, which is how the closing of an application came to need a statement of
    /// its own.
    /// </para>
    /// <para>
    /// The exclusion is restated here so that reading the candidates and writing them
    /// is not a window; the rule is re-checked for the narrower race of an account
    /// deleting its automation mid-pass, which would otherwise leave the insert
    /// pointing a foreign key at a row that has gone.
    /// </para>
    /// <para>
    /// <c>ON CONFLICT DO NOTHING</c> is the last guard, and unqualified on purpose:
    /// the only unique constraints this table has are its generated key and the slot
    /// index, so naming one would say less than absorbing both. Needed by nobody
    /// today - the job refuses to run twice at once and there is one worker - and
    /// there for the reason the sweep claims with <c>SKIP LOCKED</c>: clustering is
    /// one setting away.
    /// </para>
    /// </summary>
    private Task<int> RaiseAsync(
        List<(FollowUpCandidate Candidate, DateTimeOffset DueAt)> due,
        DateTimeOffset scannedAt,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO notifications.reminders (
                owner_id, kind, state, due_at, application_id, rule_id,
                subject_date, source_recorded_at)
            SELECT
                candidate.owner_id, 'FollowUp', 'Pending', candidate.due_at,
                candidate.application_id, candidate.rule_id, candidate.applied_date, @scanned_at
            FROM unnest(@owner_ids, @application_ids, @rule_ids, @due_ats, @applied_dates)
                 AS candidate(owner_id, application_id, rule_id, due_at, applied_date)
            WHERE NOT EXISTS (
                    SELECT 1
                    FROM notifications.reminders AS existing
                    WHERE existing.application_id = candidate.application_id
                      AND existing.kind = 'FollowUp')
              AND EXISTS (
                    SELECT 1
                    FROM notifications.reminder_rules AS rule
                    WHERE rule.id = candidate.rule_id)
            ON CONFLICT DO NOTHING
            """,
            [
                UuidArray("owner_ids", [.. due.Select(row => row.Candidate.OwnerId.Value)]),
                UuidArray("application_ids", [.. due.Select(row => row.Candidate.ApplicationId)]),
                UuidArray("rule_ids", [.. due.Select(row => row.Candidate.RuleId)]),
                InstantArray("due_ats", [.. due.Select(row => row.DueAt)]),
                DateArray("applied_dates", [.. due.Select(row => row.Candidate.AppliedDate)]),
                Instant("scanned_at", scannedAt),
            ],
            cancellationToken);

    private static NpgsqlParameter UuidArray(string name, Guid[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = values };

    private static NpgsqlParameter InstantArray(string name, DateTimeOffset[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = values };

    private static NpgsqlParameter DateArray(string name, DateOnly[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = values };

    private static NpgsqlParameter Instant(string name, DateTimeOffset value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = value };

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Raised {Raised} follow-up reminders for applications that have gone unanswered.")]
    private partial void RaisedFollowUps(int raised);
}
