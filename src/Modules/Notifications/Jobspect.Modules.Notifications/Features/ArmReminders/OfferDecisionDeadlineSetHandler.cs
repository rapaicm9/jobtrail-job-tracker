using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.ArmReminders;

/// <summary>
/// The date an offer has to be answered by. Three days before, the day before, and
/// the morning of - one more than a posting's deadline gets, because there is a job
/// at the end of this one.
/// <para>
/// Identical in shape to the posting deadline it deliberately is not folded in
/// with: the two are set at opposite ends of the pipeline and mean different things
/// to the person being reminded. A null date clears them the same way, through the
/// same no-instants-means-retract path.
/// </para>
/// </summary>
internal sealed class OfferDecisionDeadlineSetHandler(
    ReminderWriter writer, IUserProfileQuery profiles, TimeProvider timeProvider)
    : IEventHandler<OfferDecisionDeadlineSet>
{
    public async Task HandleAsync(OfferDecisionDeadlineSet integrationEvent, CancellationToken cancellationToken)
    {
        var instants = Array.Empty<ReminderInstant>() as IReadOnlyList<ReminderInstant>;

        if (integrationEvent.Deadline is { } deadline)
        {
            var timezone = await profiles.GetTimezoneAsync(integrationEvent.OwnerId, cancellationToken);
            instants = ReminderInstants.ForOfferDecision(
                deadline, timezone, timeProvider.GetUtcNow());
        }

        await writer.ApplyAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            interviewId: null,
            ReminderInstants.OfferDecisionKinds,
            instants,
            subjectAt: null,
            subjectDate: integrationEvent.Deadline,
            integrationEvent.OccurredAt,
            cancellationToken);
    }
}
