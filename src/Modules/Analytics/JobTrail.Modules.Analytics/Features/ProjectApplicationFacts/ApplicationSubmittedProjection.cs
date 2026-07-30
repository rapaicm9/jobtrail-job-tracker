using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// A newly recorded application enters the read model, carrying the dimensions no
/// other event mentions - the campaign it opened in, the company, the date the
/// user says they applied, the source and the work mode.
/// <para>
/// Usually the row's first event, but not necessarily its first: delivery is
/// unordered, so a transition may have created the row already. The upsert covers
/// both without asking which happened.
/// </para>
/// </summary>
internal sealed class ApplicationSubmittedProjection(ApplicationFactsWriter writer)
    : IEventHandler<ApplicationSubmitted>
{
    public Task HandleAsync(ApplicationSubmitted integrationEvent, CancellationToken cancellationToken) =>
        writer.SubmissionAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.CampaignId,
            integrationEvent.CompanyId,
            integrationEvent.AppliedDate,
            integrationEvent.Source,
            integrationEvent.WorkMode,
            integrationEvent.OccurredAt,
            cancellationToken);
}
