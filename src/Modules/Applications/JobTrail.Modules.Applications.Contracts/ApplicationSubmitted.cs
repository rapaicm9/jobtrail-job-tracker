using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Applications.Contracts;

/// <summary>
/// A user has recorded a new application. Analytics counts it and breaks it down;
/// Notifications arms a follow-up rule where the plan allows one - neither of
/// which the Applications module knows or names.
/// <para>
/// Published through the outbox, because a consumer that misses this one has no
/// second chance to learn the application exists: it cannot read the Applications
/// module's tables to catch up.
/// </para>
/// <para>
/// It carries ids and the few values a consumer cannot derive - the source and
/// work mode exist here because Analytics owes breakdowns on them and has no
/// other way to see them. They travel as text rather than the module's own enums,
/// which are internal by design. The role, notes and compensation are the user's
/// own account of their job search and stay out of the event stream entirely.
/// </para>
/// </summary>
public sealed record ApplicationSubmitted(
    Guid ApplicationId,
    UserId OwnerId,
    Guid CampaignId,
    Guid? CompanyId,
    DateOnly AppliedDate,
    string? Source,
    string? WorkMode,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public const string EventType = "applications.application_submitted";
}
