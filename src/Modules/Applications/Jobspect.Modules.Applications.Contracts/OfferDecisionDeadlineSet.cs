using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Applications.Contracts;

/// <summary>
/// The date by which the user has to answer an offer is now this one - or none,
/// if they cleared it. Notifications reminds them before it runs out; of the
/// deadlines this module carries, it is the one with a job at the end of it.
/// <para>
/// Kept separate from <see cref="ApplicationDeadlineSet"/> rather than folded in
/// with a "which deadline" field: the two are set at opposite ends of the
/// pipeline, mean different things to the person being reminded, and a consumer
/// that only cares about one should not have to filter the other out.
/// </para>
/// <para>
/// As with the posting deadline, a null <see cref="Deadline"/> is the statement
/// that it is gone, not an omission.
/// </para>
/// </summary>
public sealed record OfferDecisionDeadlineSet(
    Guid EventId,
    Guid ApplicationId,
    UserId OwnerId,
    DateOnly? Deadline,
    DateTimeOffset OccurredAt) : IOutboxEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public static string EventType => "applications.offer_decision_deadline_set";
}
