using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Applications.Contracts;

/// <summary>
/// An interview round is on the calendar for this instant. Notifications arms the
/// reminders for it - the day before and the hour before, at times computed from
/// the owner's timezone - and Analytics measures how long it took to get here.
/// <para>
/// The sharpest case for the outbox in the whole module: a consumer that misses
/// this one never learns the interview exists, so the reminder simply never fires,
/// and nobody finds out until the user has missed the interview.
/// </para>
/// <para>
/// It is also published when a round is moved to a new time, or brought back after
/// being cancelled, carrying the same <see cref="InterviewId"/> each time. A
/// consumer keys what it holds on that id and replaces it, so the latest of these
/// is the truth about when the round is - and no separate "rescheduled" event is
/// needed to say so.
/// </para>
/// </summary>
public sealed record InterviewScheduled(
    Guid EventId,
    Guid ApplicationId,
    Guid InterviewId,
    UserId OwnerId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset OccurredAt) : IOutboxEvent
{
    /// <summary>
    /// The name its outbox rows carry. Fixed and independent of the type name, so
    /// renaming this record never orphans rows already written.
    /// </summary>
    public static string EventType => "applications.interview_scheduled";
}
