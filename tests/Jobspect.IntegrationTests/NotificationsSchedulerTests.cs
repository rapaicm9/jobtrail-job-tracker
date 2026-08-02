using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Notifications;
using Jobspect.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The worker's schedule, against the real database. Nothing is scheduled yet -
/// the sweep and the follow-up scan arrive with the work they do - so what is
/// worth proving now is that the job store is where it should be and that a
/// schedule written to it survives being written down.
/// <para>
/// It composes the same <see cref="NotificationsScheduler.AddNotificationsScheduler"/>
/// the worker composes, rather than booting the worker: the registration is the
/// thing under test, and a scheduler that starts here starts there.
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

    [Fact]
    public async Task A_schedule_survives_being_written_down()
    {
        var jobKey = new JobKey($"probe-{Guid.CreateVersion7():N}", "tests");

        using var host = BuildSchedulerHost();
        await host.StartAsync(Ct);

        try
        {
            // Starting at all proves more than it looks: the store validates its
            // schema on the way up, so a job store in the wrong schema - or absent -
            // fails here rather than at the first fire.
            //
            // Polled rather than asserted outright, because the hosted service waits
            // for the application to finish starting before it starts the scheduler -
            // so the scheduler is running a moment after StartAsync returns, not by
            // the time it does.
            var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(Ct);
            await Poll.UntilAsync(
                () => Task.FromResult(scheduler.IsStarted),
                "the scheduler should come up once the host has started",
                Ct);

            var job = JobBuilder.Create<ProbeJob>().WithIdentity(jobKey).StoreDurably().Build();
            await scheduler.AddJob(job, replace: false, Ct);

            // Read back through the database rather than through the scheduler:
            // what is being proven is that the schedule was persisted, not that an
            // in-memory scheduler remembers what it was just told.
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            var stored = await db.Database.SqlQueryRaw<string>(
                """
                SELECT sched_name AS "Value" FROM notifications.qrtz_job_details
                WHERE job_name = {0} AND job_group = {1}
                """,
                jobKey.Name,
                jobKey.Group).ToListAsync(Ct);

            // The scheduler's name is part of the primary key of every row here, so
            // this is also the assertion that pins it: change it and every row
            // already written is orphaned.
            stored.ShouldBe(["jobspect"]);

            await scheduler.DeleteJob(jobKey, Ct);
        }
        finally
        {
            await host.StopAsync(Ct);
        }
    }

    /// <summary>
    /// A host carrying the worker's scheduler registration and nothing else,
    /// pointed at the fixture's database.
    /// </summary>
    private IHost BuildSchedulerHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(fixture.BuildSettings());
        builder.AddNotificationsScheduler();

        return builder.Build();
    }

    /// <summary>
    /// Something to schedule. It never runs - the point is that the schedule
    /// reaches the database - and the real jobs arrive with the work they do.
    /// </summary>
    private sealed class ProbeJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
