using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.RetractReminders;

/// <summary>
/// The application is closed, so nothing it still has armed is owed to anybody -
/// every kind, every round.
/// <para>
/// <b>This cannot be folded into the stage change that accompanies it.</b> Which
/// stages are terminal is the Applications module's knowledge; given only a pair of
/// stage names, nothing here could tell an application that advanced from one that
/// ended. That is why the closing is announced as a fact of its own, and why this
/// handler exists beside one consuming the same move.
/// </para>
/// <para>
/// The two arrive from one transaction sharing one instant, in either order, and both
/// retract - which is safe rather than merely tolerable. A retraction only ever moves
/// a row from armed to cancelled, so a repeat finds nothing to do; and the staleness
/// comparison is <c>&gt;=</c> precisely so the second of two events published together
/// is not refused as stale by the first. Whichever lands first, the outcome is the same.
/// </para>
/// <para>
/// <b>Reopening does not undo this.</b> A closed application brought back to life
/// stays quiet until its owner touches one of its dates, at which point the republished
/// event carries a newer occurrence and arms again. Doing better would mean recording
/// <em>why</em> a row was cancelled - the state is one word for four different things,
/// and reviving them all would resurrect reminders the owner deliberately removed.
/// </para>
/// </summary>
internal sealed class ApplicationReachedTerminalRetraction(ReminderWriter writer)
    : IEventHandler<ApplicationReachedTerminal>
{
    public Task HandleAsync(ApplicationReachedTerminal integrationEvent, CancellationToken cancellationToken) =>
        writer.RetractApplicationAsync(
            integrationEvent.ApplicationId, integrationEvent.OccurredAt, cancellationToken);
}
