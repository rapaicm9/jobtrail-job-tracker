using Quartz;

namespace Jobspect.Modules.Notifications.Features.ScanFollowUps;

/// <summary>
/// The scheduler's half of the follow-up scan, and deliberately nothing more than an
/// adapter - the second of the two triggers Quartz holds for this module, and the
/// last one it will hold. All the work is in <see cref="ReminderScan"/>, which takes
/// its clock as a constructor argument and can therefore be driven from a test on a
/// day of the test's choosing.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ReminderScanJob(ReminderScan scan) : IJob
{
    /// <summary>
    /// Nothing is caught here, as in the sweep beside it. A pass that fails has
    /// raised nothing it should not have - the insert is one statement - and the
    /// next interval finds the same silences still waiting, because an application
    /// that was not nudged is one that has no follow-up and so is still a candidate.
    /// The recovery is the schedule.
    /// </summary>
    public Task Execute(IJobExecutionContext context) =>
        scan.ScanAsync(context.CancellationToken);
}
