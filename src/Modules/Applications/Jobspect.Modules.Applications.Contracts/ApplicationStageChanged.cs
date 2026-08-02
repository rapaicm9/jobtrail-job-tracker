using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Applications.Contracts;

/// <summary>
/// An application moved to a new stage. Analytics builds the funnel and
/// time-in-stage from these; Notifications reads a move as the response it was
/// waiting for and cancels the follow-ups it had pending against the application.
/// <para>
/// Published through the outbox: a consumer that misses one cannot read this
/// module's tables to find out where the application went, so the funnel would
/// stay wrong and a follow-up would keep firing at an application that has already
/// moved on.
/// </para>
/// <para>
/// It carries <b>both</b> ends of the move rather than only the new stage, so the
/// event states the whole fact and can be applied without knowing which event came
/// before it - the property that makes unordered, repeated delivery survivable.
/// The stages travel as text because the pipeline enum is this module's own.
/// </para>
/// </summary>
public sealed record ApplicationStageChanged(
    Guid EventId,
    Guid ApplicationId,
    UserId OwnerId,
    string From,
    string To,
    DateTimeOffset OccurredAt) : IOutboxEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public static string EventType => "applications.application_stage_changed";
}
