using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// A closed application is live again, so the outcome it had ended on is no
/// longer true and comes back off the row.
/// <para>
/// The mirror of <see cref="ApplicationReachedTerminalProjection"/>: clearing the
/// outcome is the same write as setting it, with nothing in it, and it competes
/// for the same columns under the same guard - so a redelivered closure cannot
/// re-close an application that has since been reopened.
/// </para>
/// <para>
/// The funnel timestamps are untouched, and deliberately: reopening does not undo
/// having reached a stage. What happened still happened, and the figures measure
/// the first pass.
/// </para>
/// </summary>
internal sealed class ApplicationReopenedProjection(ApplicationFactsWriter writer)
    : IEventHandler<ApplicationReopened>
{
    public Task HandleAsync(ApplicationReopened integrationEvent, CancellationToken cancellationToken) =>
        writer.OutcomeAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            outcome: null,
            integrationEvent.OccurredAt,
            cancellationToken);
}
