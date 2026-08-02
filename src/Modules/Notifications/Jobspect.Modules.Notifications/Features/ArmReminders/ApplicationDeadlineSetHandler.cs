using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.ArmReminders;

/// <summary>
/// A posting's deadline was set, moved, or cleared. Three days before, and the
/// morning of.
/// <para>
/// <b>A null deadline is a statement, not an omission</b> - it says the deadline is
/// gone and its reminders should go too. It needs no branch here: no date produces
/// no instants, and a kind with no instant is retracted rather than left armed. The
/// path that handles a deadline moved closer than its own lead time is the path
/// that handles a deadline deleted.
/// </para>
/// <para>
/// The deadline rides onto the row as a date rather than an instant. It is a day,
/// not a moment, and storing one as the other would mean inventing a time and a
/// zone to read it back in.
/// </para>
/// </summary>
internal sealed class ApplicationDeadlineSetHandler(
    ReminderWriter writer, IUserProfileQuery profiles, TimeProvider timeProvider)
    : IEventHandler<ApplicationDeadlineSet>
{
    public async Task HandleAsync(ApplicationDeadlineSet integrationEvent, CancellationToken cancellationToken)
    {
        var instants = Array.Empty<ReminderInstant>() as IReadOnlyList<ReminderInstant>;

        if (integrationEvent.Deadline is { } deadline)
        {
            var timezone = await profiles.GetTimezoneAsync(integrationEvent.OwnerId, cancellationToken);
            instants = ReminderInstants.ForApplicationDeadline(
                deadline, timezone, timeProvider.GetUtcNow());
        }

        await writer.ApplyAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            interviewId: null,
            ReminderInstants.ApplicationDeadlineKinds,
            instants,
            subjectAt: null,
            subjectDate: integrationEvent.Deadline,
            integrationEvent.OccurredAt,
            cancellationToken);
    }
}
