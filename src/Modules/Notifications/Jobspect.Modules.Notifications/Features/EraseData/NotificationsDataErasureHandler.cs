using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.EraseData;

/// <summary>
/// This module's reaction to an erasure request: take everything the account has
/// out of the <c>notifications</c> schema - the reminders raised on its behalf, the
/// automation it configured, and the applications this module was watching for it.
/// One set-based delete on the owner column each.
/// <para>
/// Idempotent, as at-least-once delivery requires: erasing an already-erased
/// account deletes nothing and returns quietly.
/// </para>
/// <para>
/// <b>It matters that this runs after the Applications module's erasure, and the
/// ordering is arranged in the host</b> (see the registration order in the API's
/// composition root). That module's erasure deletes the events still owed on the
/// account's behalf; the other way round, an owed <c>InterviewScheduled</c>
/// delivered in the gap would arm a reminder for an account that had just been
/// erased - and unlike a stale read-model row, that one goes on to notify somebody.
/// </para>
/// <para>
/// Deliveries are not deleted here and their absence is deliberate. A delivery says
/// nothing without its reminder, so the foreign key cascades and the database takes
/// them as the reminders go; a delete here would be a second statement doing work
/// that already happened.
/// </para>
/// </summary>
internal sealed class NotificationsDataErasureHandler(NotificationsDbContext dbContext)
    : IEventHandler<UserDataDeletionRequested>
{
    public async Task HandleAsync(
        UserDataDeletionRequested integrationEvent, CancellationToken cancellationToken)
    {
        var ownerId = integrationEvent.OwnerId;

        await dbContext.Reminders
            .Where(reminder => reminder.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ReminderRules
            .Where(rule => rule.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.TrackedApplications
            .Where(tracked => tracked.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
