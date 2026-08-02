using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Applications.Contracts;

/// <summary>
/// A scheduled interview round is off. Notifications drops the reminders it armed
/// for it, keyed by the same <see cref="InterviewId"/> those were armed under.
/// <para>
/// The retraction to <see cref="InterviewScheduled"/>, and the only one there is:
/// a round has no delete, so cancelling its outcome is how a user takes it off
/// the calendar. Without this event that cancellation would reach nobody, and the
/// reminder would fire for an interview that is not happening.
/// </para>
/// <para>
/// Recording that a round <em>happened</em> - passed or failed - is deliberately
/// not this event. An outcome is entered after the fact, so the instant a reminder
/// was armed for has already gone by, and there is nothing left to retract.
/// </para>
/// </summary>
public sealed record InterviewCancelled(
    Guid EventId,
    Guid ApplicationId,
    Guid InterviewId,
    UserId OwnerId,
    DateTimeOffset OccurredAt) : IOutboxEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public static string EventType => "applications.interview_cancelled";
}
