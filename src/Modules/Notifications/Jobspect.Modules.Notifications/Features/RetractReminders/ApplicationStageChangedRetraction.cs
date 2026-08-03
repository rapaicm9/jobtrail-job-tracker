using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.RetractReminders;

/// <summary>
/// The application moved, so the nudge about it not moving is moot.
/// <para>
/// <b>The follow-up and nothing else.</b> A move is the answer the follow-up was
/// waiting for, and it says nothing at all about the other reminders: an application
/// advancing from Applied to Interview needs its interview alerts <em>more</em>, and
/// its posting deadline is a date the owner asked to be reminded of whatever stage
/// the application is in. Retracting those here would be the failure this handler is
/// most likely to be written as.
/// </para>
/// <para>
/// The stages the event carries are read by nothing here. A move is a move, including
/// a reopening - which lands on Applied with nothing pending, so this is a no-op there
/// rather than a case to exclude. That also keeps this module out of the business of
/// knowing which stage names mean what, which is the whole reason the closing of an
/// application is announced as its own event rather than inferred from a pair of names.
/// </para>
/// <para>
/// It retracts a kind nothing raises yet - follow-ups arrive with the rule and the
/// scan that create them. Built now because the event is already flowing and the rule
/// should not have to remember to bring its own retraction.
/// </para>
/// </summary>
internal sealed class ApplicationStageChangedRetraction(ReminderWriter writer)
    : IEventHandler<ApplicationStageChanged>
{
    private static readonly ReminderKind[] Answered = [ReminderKind.FollowUp];

    public Task HandleAsync(ApplicationStageChanged integrationEvent, CancellationToken cancellationToken) =>
        writer.RetractAsync(
            integrationEvent.ApplicationId,
            interviewId: null,
            Answered,
            integrationEvent.OccurredAt,
            cancellationToken);
}
