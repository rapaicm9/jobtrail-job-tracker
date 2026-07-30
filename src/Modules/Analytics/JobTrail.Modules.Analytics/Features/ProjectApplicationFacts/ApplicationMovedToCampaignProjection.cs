using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// An application changed campaign, so the figures scoped to a campaign follow
/// it. This module is the consumer that event was published ahead of - a campaign
/// is a whole job search, and an account running more than one is asking how this
/// one compares to the last.
/// <para>
/// Only the move's destination is needed: the event states both ends so it can be
/// applied without having seen the moves before it, and the row is overwritten
/// rather than adjusted, which is what lets a redelivery change nothing.
/// </para>
/// </summary>
internal sealed class ApplicationMovedToCampaignProjection(ApplicationFactsWriter writer)
    : IEventHandler<ApplicationMovedToCampaign>
{
    public Task HandleAsync(ApplicationMovedToCampaign integrationEvent, CancellationToken cancellationToken) =>
        writer.CampaignAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.ToCampaignId,
            integrationEvent.OccurredAt,
            cancellationToken);
}
