using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.DismissReminder;

/// <summary>
/// Clears one entry from the caller's feed.
/// <para>
/// <b>Only a delivered reminder can be dismissed</b>, and the ownership check is
/// inside the query, so a reminder belonging to someone else and one that was never
/// delivered are both simply not found. That is the honest answer for the second case
/// as well as the first: the feed is what the owner was told, and a row that was
/// armed, retracted, or dropped as too late has no entry there to clear.
/// </para>
/// <para>
/// Dismissing twice is not an error. The second call finds the entry already
/// dismissed and returns it unchanged - a client retrying a request whose response it
/// never saw must not be told the reminder has vanished.
/// </para>
/// </summary>
internal sealed class DismissReminderHandler(NotificationsDbContext dbContext)
{
    public async Task<Result<ReminderResponse>> HandleAsync(
        UserId ownerId, Guid id, CancellationToken cancellationToken)
    {
        var reminder = await dbContext.Reminders.FirstOrDefaultAsync(
            candidate => candidate.Id == id
                && candidate.OwnerId == ownerId
                && (candidate.State == ReminderState.Sent || candidate.State == ReminderState.Dismissed),
            cancellationToken);

        if (reminder is null)
        {
            return ReminderErrors.NotFound(id);
        }

        if (reminder.State is ReminderState.Sent)
        {
            reminder.State = ReminderState.Dismissed;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return reminder.ToResponse();
    }
}
