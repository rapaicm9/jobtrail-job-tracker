using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Identity;
using Jobspect.Modules.Notifications;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The worker's schedule, against the real database: that the job store is where it
/// should be, that a schedule written to it survives being written down, and that the
/// sweep is genuinely on a trigger rather than merely written.
/// <para>
/// It composes the same <see cref="NotificationsScheduler.AddNotificationsScheduler"/>
/// the worker composes, rather than booting the worker: the registration is the
/// thing under test, and a scheduler that starts here starts there.
/// </para>
/// <para>
/// These are the only tests that run a sweep on the real clock. What the sweep
/// <em>decides</em> is pinned in <see cref="NotificationsSweepTests"/>, at a time of
/// that suite's choosing; all that is left for here is whether anything ever calls it.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsSchedulerTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every table Quartz's PostgreSQL job store expects, at its own prefix.</summary>
    private static readonly string[] JobStoreTables =
    [
        "qrtz_blob_triggers",
        "qrtz_calendars",
        "qrtz_cron_triggers",
        "qrtz_fired_triggers",
        "qrtz_job_details",
        "qrtz_locks",
        "qrtz_paused_trigger_grps",
        "qrtz_scheduler_state",
        "qrtz_simple_triggers",
        "qrtz_simprop_triggers",
        "qrtz_triggers",
    ];

    [Fact]
    public async Task The_job_store_lives_in_this_modules_schema()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var mine = await db.Database.SqlQueryRaw<string>(
            """
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'notifications' AND table_name LIKE 'qrtz_%'
            ORDER BY table_name
            """).ToListAsync(Ct);

        mine.ShouldBe(JobStoreTables);

        // And nowhere else. A scheduler configured without the schema-qualified
        // prefix would put all eleven in the default schema and work perfectly,
        // which is exactly why this half of the assertion is here: one module, one
        // schema, including the tables this module did not design.
        var strays = await db.Database.SqlQueryRaw<string>(
            """
            SELECT table_schema AS "Value" FROM information_schema.tables
            WHERE table_name LIKE 'qrtz_%' AND table_schema <> 'notifications'
            """).ToListAsync(Ct);

        strays.ShouldBeEmpty();
    }

    /// <summary>
    /// The whole schedule, end to end: both triggers are written down, they survive
    /// being written down, and the sweep fires the work it was written for.
    /// <para>
    /// One test rather than four because it can only be one host. Quartz caches the
    /// logger factory it was first given in a static, so a second scheduler host in
    /// the same process comes up holding the first one's - disposed - and fails on the
    /// way in. That is a property of the library rather than of this suite, and it is
    /// why the follow-up scan's registration is asserted here rather than in a class
    /// of its own.
    /// </para>
    /// <para>
    /// Only the sweep is driven to its effect. What the scan <em>decides</em> is
    /// pinned in <see cref="NotificationsScanTests"/> on a day of that suite's
    /// choosing; a scan on the real clock would raise follow-ups against whatever the
    /// rest of the suite has left lying about, which proves nothing and disturbs
    /// everything.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Both_jobs_are_scheduled_durably_and_the_sweep_fires()
    {
        // Due already, on the real clock, because this one has to survive a real
        // trigger firing at a moment nothing here chooses - but well inside the
        // lateness the sweep still delivers within, however long the host takes.
        var applicationId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            db.Reminders.Add(new Reminder
            {
                OwnerId = UserId.New(),
                Kind = ReminderKind.ApplicationDeadlineMorningOf,
                DueAt = DateTimeOffset.UtcNow,
                ApplicationId = applicationId,
                SourceRecordedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(Ct);
        }

        using var host = BuildSchedulerHost();

        // Starting at all proves more than it looks: the job store validates its
        // schema on the way up, so a store in the wrong schema - or absent - fails
        // here rather than at the first fire.
        await host.StartAsync(Ct);

        try
        {
            // Read back through the database rather than through the scheduler: what
            // is being proven is that the schedule was persisted, not that an
            // in-memory scheduler remembers what it was just told. Polled because the
            // hosted service waits for the application to finish starting before it
            // starts the scheduler, so nothing is written by the time StartAsync
            // returns.
            //
            // The scheduler's name is part of the primary key of every row here, so
            // this also pins it: change it and every row already written is orphaned.
            await Poll.UntilAsync(
                async () => await ScheduledUnderAsync(SweepJobQuery) == "jobspect"
                    && await ScheduledUnderAsync(SweepTriggerQuery) == "jobspect"
                    && await ScheduledUnderAsync(ScanJobQuery) == "jobspect"
                    && await ScheduledUnderAsync(ScanTriggerQuery) == "jobspect",
                "both jobs and their triggers should reach the persistent store",
                Ct);

            // And the trigger reaches the job. It starts now and repeats, so the first
            // pass happens as the scheduler comes up rather than an interval later.
            await Poll.UntilAsync(
                async () => await StateAsync(applicationId) == ReminderState.Sent,
                "the scheduled sweep should deliver a reminder that has come due",
                Ct);
        }
        finally
        {
            await host.StopAsync(Ct);
        }
    }

    /// <summary>
    /// The job the module registers, and the trigger pointed at it. Two literal
    /// statements rather than one parameterised by table name: a table name cannot be
    /// a parameter, and building it by interpolation is what EF1002 exists to stop.
    /// </summary>
    private const string SweepJobQuery =
        """
        SELECT sched_name AS "Value" FROM notifications.qrtz_job_details
        WHERE job_name = 'reminder-sweep' AND job_group = 'notifications'
        """;

    private const string SweepTriggerQuery =
        """
        SELECT sched_name AS "Value" FROM notifications.qrtz_triggers
        WHERE job_name = 'reminder-sweep' AND job_group = 'notifications'
        """;

    private const string ScanJobQuery =
        """
        SELECT sched_name AS "Value" FROM notifications.qrtz_job_details
        WHERE job_name = 'follow-up-scan' AND job_group = 'notifications'
        """;

    private const string ScanTriggerQuery =
        """
        SELECT sched_name AS "Value" FROM notifications.qrtz_triggers
        WHERE job_name = 'follow-up-scan' AND job_group = 'notifications'
        """;

    /// <summary>
    /// The scheduler name the sweep was written down under, or null while it has not
    /// been written down at all.
    /// </summary>
    private async Task<string?> ScheduledUnderAsync(string sql)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var names = await db.Database.SqlQueryRaw<string>(sql).ToListAsync(Ct);

        return names.SingleOrDefault();
    }

    private async Task<ReminderState> StateAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await db.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.ApplicationId == applicationId)
            .Select(reminder => reminder.State)
            .SingleAsync(Ct);
    }

    /// <summary>
    /// The worker's composition root, pointed at the fixture's database: the store,
    /// the one read it makes across a module boundary, and the schedule - in that
    /// order and nothing else. All three, because a worker missing any of them does
    /// not start, which is itself part of what these tests assert.
    /// <para>
    /// The profile read is not optional scenery. The follow-up scan raises reminders
    /// at 11:00 in the owner's own timezone, so its job cannot be constructed without
    /// <c>IUserProfileQuery</c> - and container validation would refuse this host on
    /// the way up rather than failing at the first fire.
    /// </para>
    /// </summary>
    private IHost BuildSchedulerHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(fixture.BuildSettings());
        builder.AddNotificationsModule();
        builder.AddIdentityProfileQuery();
        builder.AddNotificationsScheduler();

        return builder.Build();
    }
}
