using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.RetractReminders;

/// <summary>
/// A round is off, so the two things this module had to say about it are not worth
/// saying.
/// <para>
/// <b>That round only.</b> The reminders are keyed by the interview id they were armed
/// under, so another round on the same application keeps its own pair - which is the
/// difference between this and the closing of the application it belongs to.
/// </para>
/// <para>
/// It retracts rather than deletes, and the round being put back on is not this
/// handler's problem: a cancelled interview returned to awaited is republished as
/// <see cref="InterviewScheduled"/> carrying the same id and a newer occurrence, so
/// the arming path re-arms it through the ordinary slot rules.
/// </para>
/// <para>
/// This is the event's first consumer anywhere in the system. Analytics declines it on
/// purpose - the column it would touch records that a round was once booked, which
/// cancelling does not undo - so until now it was published and read by nobody.
/// </para>
/// </summary>
internal sealed class InterviewCancelledRetraction(ReminderWriter writer)
    : IEventHandler<InterviewCancelled>
{
    public Task HandleAsync(InterviewCancelled integrationEvent, CancellationToken cancellationToken) =>
        writer.RetractAsync(
            integrationEvent.ApplicationId,
            integrationEvent.InterviewId,
            ReminderInstants.InterviewKinds,
            integrationEvent.OccurredAt,
            cancellationToken);
}
