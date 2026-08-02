using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Applications.Contracts;

/// <summary>
/// An application is closed: it ended on an outcome, or an outcome it already had
/// was corrected to another. Notifications drops everything still pending for it -
/// follow-ups, deadline reminders, interview alerts - and Analytics counts the
/// outcome and stops the clocks it was running.
/// <para>
/// It accompanies <see cref="ApplicationStageChanged"/> rather than replacing it,
/// because <em>which stages are terminal</em> is this module's knowledge: given
/// only a pair of stage names, a consumer cannot tell an application that
/// advanced from one that just ended. Both are recorded in the same transaction as
/// the move, and they may be delivered in either order - each states its own fact
/// in full so that does not matter.
/// </para>
/// </summary>
public sealed record ApplicationReachedTerminal(
    Guid EventId,
    Guid ApplicationId,
    UserId OwnerId,
    string From,
    string Outcome,
    DateTimeOffset OccurredAt) : IOutboxEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public static string EventType => "applications.application_reached_terminal";
}
