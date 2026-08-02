using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Applications.Contracts;

/// <summary>
/// An application's posting deadline is now this date - or none, if the user
/// cleared it. Notifications schedules the reminder from it, at an instant
/// computed from the owner's timezone, because a date only means something in a
/// place.
/// <para>
/// Published through the outbox: a missed one is a reminder that silently never
/// exists, and nobody discovers that until the deadline has passed.
/// </para>
/// <para>
/// <see cref="Deadline"/> is nullable and a null one is not an omission - it says
/// the deadline is gone and whatever was scheduled for it should be dropped.
/// Without that, removing a deadline would leave a consumer holding a reminder it
/// was never told to cancel.
/// </para>
/// </summary>
public sealed record ApplicationDeadlineSet(
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
    public static string EventType => "applications.application_deadline_set";
}
