using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Applications.Contracts;

/// <summary>
/// A closed application is live again: the outcome it had ended on was undone and
/// it re-entered the pipeline. Analytics puts it back in the funnel it had been
/// counted out of, and Notifications may arm again what it cancelled when the
/// application closed.
/// <para>
/// The mirror of <see cref="ApplicationReachedTerminal"/>, and recorded for the
/// same reason: a consumer holding only the two stage names has no way to know
/// that a move out of <c>Ghosted</c> is a resurrection rather than an ordinary
/// step.
/// </para>
/// </summary>
public sealed record ApplicationReopened(
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
    public static string EventType => "applications.application_reopened";
}
