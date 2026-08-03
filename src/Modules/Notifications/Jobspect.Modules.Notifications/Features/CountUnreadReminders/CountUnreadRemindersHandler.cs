using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.CountUnreadReminders;

/// <summary>
/// How many reminders this account has been told and not yet cleared.
/// <para>
/// Unread is <c>Sent</c>, and that is the whole definition - a delivered reminder the
/// owner has not dismissed. There is no read flag beside the state because there is
/// nothing a second column could say that this one does not.
/// </para>
/// </summary>
internal sealed class CountUnreadRemindersHandler(NotificationsDbContext dbContext)
{
    public async Task<UnreadCountResponse> HandleAsync(UserId ownerId, CancellationToken cancellationToken) =>
        new(await dbContext.Reminders
            .AsNoTracking()
            .CountAsync(
                reminder => reminder.OwnerId == ownerId && reminder.State == ReminderState.Sent,
                cancellationToken));
}
