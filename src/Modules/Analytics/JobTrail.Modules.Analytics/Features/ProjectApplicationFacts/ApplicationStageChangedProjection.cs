using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// A move through the pipeline: where the application is now, and the funnel
/// timestamps reaching that stage implies.
/// <para>
/// Every accepted move is announced as one of these, including the ones that also
/// close or reopen an application - so this is the only event that needs to write
/// the stage, and the outcome is left to the two events that know what a terminal
/// stage is.
/// </para>
/// <para>
/// Only the move's <em>to</em> end is read. The <em>from</em> end proves where the
/// application had been but not when it got there, and a funnel timestamp guessed
/// from it would be invented.
/// </para>
/// </summary>
internal sealed class ApplicationStageChangedProjection(ApplicationFactsWriter writer)
    : IEventHandler<ApplicationStageChanged>
{
    public Task HandleAsync(ApplicationStageChanged integrationEvent, CancellationToken cancellationToken) =>
        writer.StageAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.To,
            integrationEvent.OccurredAt,
            cancellationToken);
}
