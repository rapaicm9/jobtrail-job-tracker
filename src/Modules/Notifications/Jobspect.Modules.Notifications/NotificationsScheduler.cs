using Jobspect.Modules.Notifications.Features.SweepReminders;
using Jobspect.Modules.Notifications.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Jobspect.Modules.Notifications;

/// <summary>
/// The schedule this module's recurring work runs on. Separate from
/// <see cref="NotificationsModule.AddNotificationsModule"/> and called by the
/// worker alone: both hosts need the store, but only one may run the schedule.
/// Two schedulers over one job store, each believing itself the only one, is
/// exactly the double delivery the whole design is arranged to avoid.
/// <para>
/// It lives in this module rather than in the worker because the jobs are this
/// module's own and the job store's tables are in this module's schema. The worker
/// composes; it never names a scheduler type.
/// </para>
/// <para>
/// Quartz holds exactly two triggers, and each arrives with the work it runs: the
/// due-reminder sweep below, and the follow-up scan still to come. There is no
/// trigger per reminder - the row is the reminder (ADR 0006).
/// </para>
/// </summary>
public static class NotificationsScheduler
{
    /// <summary>
    /// The scheduler's name, and part of the primary key of every row the job
    /// store writes. Changing it does not rename anything: it orphans every
    /// trigger, job and lock already recorded, and the scheduler comes up believing
    /// it has never run. Pinned here, deliberately not derived from the machine,
    /// the environment or the assembly.
    /// </summary>
    private const string SchedulerName = "jobspect";

    /// <summary>
    /// How often the sweep looks for reminders that have come due, and so the
    /// precision the whole feature fires at. A minute is far below what the reminders
    /// themselves are stated in - the morning before, an hour before - and it sits
    /// well inside the lateness the sweep will still deliver within.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Stable, because it is the key the job store writes its rows under. Grouped by
    /// the module rather than left in Quartz's default group, so the scheduler's
    /// tables read the same way its schema does.
    /// </summary>
    private static readonly JobKey SweepJob = new("reminder-sweep", NotificationsDbContext.Schema);

    public static IHostApplicationBuilder AddNotificationsScheduler(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobspect")
            ?? throw new InvalidOperationException(
                "Connection string 'jobspect' is not configured. It is injected by the AppHost.");

        // The work the sweep job adapts, and the clock it judges lateness against.
        // The clock is registered defensively - this host may be the only one that
        // composes anything at all, and the module should stand up either way.
        builder.Services.AddScoped<ReminderSweep>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddQuartz(quartz =>
        {
            quartz.SchedulerName = SchedulerName;

            // Persistent, because an in-memory schedule is lost on restart and the
            // sweep is the only thing standing between a due reminder and silence.
            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(postgres =>
                {
                    postgres.ConnectionString = connectionString;

                    // One module, one schema - including the scheduler's tables. A
                    // schema-qualified prefix is the only way to say that: the job
                    // store builds its SQL by concatenating this in front of each
                    // table name, and has no notion of a schema otherwise.
                    postgres.TablePrefix = $"{NotificationsDbContext.Schema}.qrtz_";
                });

                // Job data as strings only. Nothing scheduled here carries any -
                // both jobs find their own work in this module's tables - so this
                // is a promise kept rather than a constraint felt.
                store.UseProperties = true;
                store.UseSystemTextJsonSerializer();

                // Fail on the way up if the job store's tables are missing, rather
                // than at the first fire. The migration one-shot runs before this
                // host starts, so the only way to see this is a genuinely
                // unmigrated database - which is worth a loud start-up failure and
                // not a reminder that quietly never arrives.
                store.PerformSchemaValidation = true;

                // No clustering while there is one worker. It is opt-in, and
                // turning it on the day there are two is configuration rather than
                // redesign - much of why a real scheduler was taken at all.
            });

            // Registered on every start-up rather than once: Quartz overwrites
            // existing scheduling data by default, so this is an upsert into the
            // persistent store and not a second copy of the same schedule.
            quartz.AddJob<ReminderSweepJob>(job => job
                .WithIdentity(SweepJob)
                .WithDescription("Delivers the reminders that have come due; drops the ones too late to send."));

            quartz.AddTrigger(trigger => trigger
                .WithIdentity($"{SweepJob.Name}-trigger", SweepJob.Group)
                .ForJob(SweepJob)

                // Immediately, then on the interval. A worker that has just come back
                // up has the best reason of any to look: whatever came due while it
                // was down is either still deliverable or about to stop being.
                .StartNow()
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(SweepInterval)
                    .RepeatForever()));
        });

        // Let a running job finish when the host is asked to stop. A sweep
        // interrupted mid-delivery would be redelivered on the next pass and is
        // safe either way, but finishing is cheap and keeps the logs honest.
        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return builder;
    }
}
