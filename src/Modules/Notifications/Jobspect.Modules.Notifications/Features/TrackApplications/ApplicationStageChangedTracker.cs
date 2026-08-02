using Jobspect.Modules.Applications.Contracts;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.TrackApplications;

/// <summary>
/// The application moved, so it is no longer waiting for the answer the follow-up
/// scan exists to notice the absence of.
/// <para>
/// Only the destination is recorded. The event states both ends of the move so it
/// can be applied without knowing what came before it, but this table holds where
/// an application <em>is</em>, and the stage it came from is a fact about its
/// history that nothing here asks.
/// </para>
/// <para>
/// Retracting the reminders an answered application still has pending is a separate
/// concern from this one and arrives with the retraction slice; this handler only
/// keeps the scan's record straight.
/// </para>
/// </summary>
internal sealed class ApplicationStageChangedTracker(TrackedApplicationWriter writer)
    : IEventHandler<ApplicationStageChanged>
{
    public Task HandleAsync(ApplicationStageChanged integrationEvent, CancellationToken cancellationToken) =>
        writer.StageChangedAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.To,
            integrationEvent.OccurredAt,
            cancellationToken);
}
