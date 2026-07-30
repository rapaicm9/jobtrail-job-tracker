using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// An interview round went on the calendar, which is how long it took to get one
/// booked - not the same question as when the application reached the Interview
/// stage, and worth its own column for that reason.
/// <para>
/// The event is published again whenever a round is moved or brought back, so
/// this handler sees several for one application. It keeps the earliest, which is
/// the one the figure asks about.
/// </para>
/// <para>
/// <c>InterviewCancelled</c> is deliberately not consumed. A round having once
/// been booked is not undone by its cancellation, and un-setting the column would
/// destroy the very property - that the earliest value always wins - which makes
/// this projection safe under redelivery and out-of-order arrival.
/// </para>
/// </summary>
internal sealed class InterviewScheduledProjection(ApplicationFactsWriter writer)
    : IEventHandler<InterviewScheduled>
{
    public Task HandleAsync(InterviewScheduled integrationEvent, CancellationToken cancellationToken) =>
        writer.InterviewScheduledAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.ScheduledAt,
            cancellationToken);
}
