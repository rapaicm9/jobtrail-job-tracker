using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// An application closed on an outcome, which stops the clocks the time-based
/// figures were running and puts it in the funnel's last column.
/// <para>
/// It exists because <em>which stages are terminal</em> is the Applications
/// module's knowledge: given a pair of stage names this module cannot tell an
/// advance from an ending. It accompanies the stage change rather than replacing
/// it, arrives in either order, and writes only the two columns the stage change
/// does not - so whichever lands first, both facts survive.
/// </para>
/// </summary>
internal sealed class ApplicationReachedTerminalProjection(ApplicationFactsWriter writer)
    : IEventHandler<ApplicationReachedTerminal>
{
    public Task HandleAsync(ApplicationReachedTerminal integrationEvent, CancellationToken cancellationToken) =>
        writer.OutcomeAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.Outcome,
            integrationEvent.OccurredAt,
            cancellationToken);
}
