using Jobspect.Infrastructure.Persistence;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.ListReminders;

/// <summary>
/// A page of what this module has told one account, newest first - the order a feed
/// is read in, with the id breaking ties because reminders raised by one event share
/// an instant and the reading must still be stable.
/// <para>
/// <b>The two delivered states, and only those.</b> A reminder is something the owner
/// was told; the other three states are things that were not said. <c>Pending</c> has
/// not happened yet, <c>Cancelled</c> was retracted before it could, and
/// <c>Dropped</c> was owed and deliberately withheld as too late - showing that last
/// one would be exactly the noise the lateness rule exists to prevent, and it stays a
/// state of its own so an operator can tell it from a retraction, not so a reader
/// sees it.
/// </para>
/// <para>
/// Ownership is inside the query rather than checked beside it, so another account's
/// reminders are not hidden from the response - they are never read.
/// </para>
/// </summary>
internal sealed class ListRemindersHandler(NotificationsDbContext dbContext)
{
    /// <summary>The states that mean "the owner was told this".</summary>
    private static readonly ReminderState[] Delivered = [ReminderState.Sent, ReminderState.Dismissed];

    public async Task<PagedResponse<ReminderResponse>> HandleAsync(
        UserId ownerId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = dbContext.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.OwnerId == ownerId && Delivered.Contains(reminder.State));

        if (page.Position is { } position && PagingParameters.SortKeyToInstant(position.SortKey) is { } dueAt)
        {
            var lastId = position.Id;
            query = query.Where(reminder =>
                reminder.DueAt < dueAt || (reminder.DueAt == dueAt && reminder.Id < lastId));
        }

        // Rows map after materialization: the stored kind is a converted enum, so the
        // string projection cannot happen in SQL.
        return await PageBuilder.BuildAsync(
            query.OrderByDescending(reminder => reminder.DueAt).ThenByDescending(reminder => reminder.Id),
            page.Limit,
            reminder => reminder.ToResponse(),
            reminder => new Cursor(reminder.Id, PagingParameters.SortKeyFrom(reminder.DueAt)),
            cancellationToken);
    }
}
