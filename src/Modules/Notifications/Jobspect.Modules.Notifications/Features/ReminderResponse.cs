using Jobspect.Modules.Notifications.Domain;

namespace Jobspect.Modules.Notifications.Features;

/// <summary>
/// One entry in the feed: something this module told its owner, and enough to say
/// what it was about.
/// <para>
/// <b>There is no state here, only <see cref="Dismissed"/>.</b> The feed holds the two
/// delivered states and nothing else, so the five-value column collapses to the one
/// bit a reader needs - and the internal vocabulary, in which "Sent" means unread,
/// stays where it belongs. Unread is simply not dismissed, which is also what the
/// count endpoint counts.
/// </para>
/// </summary>
/// <param name="Id">What a dismissal addresses.</param>
/// <param name="Kind">What the reminder is for, as its name.</param>
/// <param name="DueAt">
/// The moment it was for. Deliberately shown rather than when it was delivered: this is
/// what the feed is ordered by, and a timestamp differing from the sort key reads as a
/// bug. The two are within the sweep's tolerance of one another in any case.
/// </param>
/// <param name="ApplicationId">What the client deep-links to.</param>
/// <param name="InterviewId">The round, on the two interview kinds only.</param>
/// <param name="SubjectAt">
/// When the interview actually is. The reason this column is carried on the row at all:
/// nothing can recover it later, and without it the feed could say a reminder is about
/// an interview but not when.
/// </param>
/// <param name="SubjectDate">The deadline the reminder is about, on the kinds whose subject is a day.</param>
/// <param name="Dismissed">Whether the owner has cleared it.</param>
internal sealed record ReminderResponse(
    Guid Id,
    ReminderKind Kind,
    DateTimeOffset DueAt,
    Guid ApplicationId,
    Guid? InterviewId,
    DateTimeOffset? SubjectAt,
    DateOnly? SubjectDate,
    bool Dismissed);

internal static class ReminderResponseMapping
{
    /// <summary>
    /// Maps after materialization rather than in the query: <see cref="ReminderResponse.Dismissed"/>
    /// is a comparison against a converted enum, which has no SQL equivalent to
    /// translate to.
    /// </summary>
    public static ReminderResponse ToResponse(this Reminder reminder) =>
        new(
            reminder.Id,
            reminder.Kind,
            reminder.DueAt,
            reminder.ApplicationId,
            reminder.InterviewId,
            reminder.SubjectAt,
            reminder.SubjectDate,
            reminder.State is ReminderState.Dismissed);
}
