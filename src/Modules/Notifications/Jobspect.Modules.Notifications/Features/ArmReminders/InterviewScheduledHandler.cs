using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.ArmReminders;

/// <summary>
/// A round is on the calendar, so there are two things to say about it: the
/// morning before, and an hour before.
/// <para>
/// The same event announces a round that has <em>moved</em>, carrying the same
/// interview id, so this handler sees several for one round and the latest wins.
/// The slot is keyed by that id, which is what makes a reschedule replace its own
/// alerts rather than accumulate a second pair beside them.
/// </para>
/// <para>
/// The round's own instant is carried onto the row as its subject. Nothing can
/// recover it later - this module cannot read the Applications module's tables and
/// the outbox prunes what it has delivered - so the feed would otherwise be able to
/// say a reminder is about an interview but not when the interview is.
/// </para>
/// </summary>
internal sealed class InterviewScheduledHandler(
    ReminderWriter writer, IUserProfileQuery profiles, TimeProvider timeProvider)
    : IEventHandler<InterviewScheduled>
{
    public async Task HandleAsync(InterviewScheduled integrationEvent, CancellationToken cancellationToken)
    {
        var timezone = await profiles.GetTimezoneAsync(integrationEvent.OwnerId, cancellationToken);

        var instants = ReminderInstants.ForInterview(
            integrationEvent.ScheduledAt, timezone, timeProvider.GetUtcNow());

        await writer.ApplyAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.InterviewId,
            ReminderInstants.InterviewKinds,
            instants,
            subjectAt: integrationEvent.ScheduledAt,
            subjectDate: null,
            integrationEvent.OccurredAt,
            cancellationToken);
    }
}
