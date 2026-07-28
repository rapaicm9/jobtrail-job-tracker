using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Applications.Contracts;

/// <summary>
/// An application changed campaign - moved by its owner, or swept to the default
/// because the campaign it sat in was deleted. Analytics is the consumer this
/// exists for: <c>ApplicationSubmitted</c> carries the campaign an application
/// opened in, and without this event that first attribution would be the only one
/// it ever hears.
/// <para>
/// Published through the outbox, and the reason is worth stating because it is not
/// the usual one. Nothing schedules a reminder from a campaign move, so a miss
/// costs nobody anything today. But a read model that is rebuilt from its event
/// stream can only ever be as right as that stream, and a move that was never
/// announced cannot be replayed later - the fact is simply gone. That is the gap
/// this closes, and it has to be closed before the moves happen rather than when
/// the consumer is written.
/// </para>
/// <para>
/// It carries both ends of the move, so it states a whole fact and can be applied
/// without having seen the moves before it.
/// </para>
/// </summary>
public sealed record ApplicationMovedToCampaign(
    Guid EventId,
    Guid ApplicationId,
    UserId OwnerId,
    Guid FromCampaignId,
    Guid ToCampaignId,
    DateTimeOffset OccurredAt) : IOutboxEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public static string EventType => "applications.application_moved_to_campaign";
}
