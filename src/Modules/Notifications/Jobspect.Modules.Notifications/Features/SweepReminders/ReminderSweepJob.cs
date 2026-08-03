using Quartz;

namespace Jobspect.Modules.Notifications.Features.SweepReminders;

/// <summary>
/// The scheduler's half of the sweep, and deliberately nothing more than an adapter.
/// <para>
/// All the work is in <see cref="ReminderSweep"/>, which takes its clock as a
/// constructor argument and can therefore be driven straight from a test at a time of
/// the test's choosing. Everything this job decides - when to run, and not to run
/// twice at once - is what a scheduler is for; everything the sweep decides is what
/// this module is for.
/// </para>
/// <para>
/// It holds no job data and looks nothing up in the trigger: the work is whatever the
/// reminder table owes right now, which is a question only the database can answer.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ReminderSweepJob(ReminderSweep sweep) : IJob
{
    /// <summary>
    /// Nothing is caught here. A pass that fails leaves every row it did not reach
    /// still <c>Pending</c>, Quartz records the failure against the job key, and the
    /// next interval claims them again - so the recovery is the schedule itself,
    /// rather than a retry loop written twice.
    /// </summary>
    public Task Execute(IJobExecutionContext context) =>
        sweep.SweepAsync(context.CancellationToken);
}
