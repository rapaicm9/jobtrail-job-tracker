using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.ClearReminderRule;

/// <summary>
/// Turns the automation off: takes back the nudges it has raised but not yet
/// delivered, then removes the rule that would raise more.
/// <para>
/// Deleting the row rather than clearing a column, so "no automation" has one
/// representation and not two - the rule carries no enabled flag for the same
/// reason.
/// </para>
/// </summary>
internal sealed class ClearReminderRuleHandler(NotificationsDbContext dbContext, TimeProvider timeProvider)
{
    /// <summary>
    /// <b>Pending follow-ups go with the rule; delivered ones stay.</b> The owner has
    /// said stop, and a nudge still sitting in the schedule has not been said yet - so
    /// firing it tomorrow would be the automation outliving the decision to end it.
    /// What has already reached the feed is a record of something they were told, and
    /// ADR 0006 does not rewrite those; the foreign key nulls their <c>rule_id</c> as
    /// the rule goes and they read exactly as before.
    /// <para>
    /// Scoped by the rule rather than by owner and kind. The two select the same rows
    /// while an account may hold one rule, and only one of them still says what it
    /// means on the day that cap is lifted.
    /// </para>
    /// <para>
    /// No staleness guard, unlike every other retraction in this module. Those defend
    /// against an event redelivered out of order; this is a request, and the only
    /// other writer of a follow-up slot is the scan - which cannot arm one for a rule
    /// that is being deleted, because its insert re-checks that the rule is still
    /// there.
    /// </para>
    /// </summary>
    public async Task ClearAsync(UserId ownerId, CancellationToken cancellationToken)
    {
        var ruleId = await dbContext.ReminderRules
            .Where(rule => rule.OwnerId == ownerId)
            .Select(rule => (Guid?)rule.Id)
            .SingleOrDefaultAsync(cancellationToken);

        // Deleting a rule that is not there is success, not a 404: the caller asked
        // to be left without one and they are, and a client retrying a dropped
        // response should not be told off for it.
        if (ruleId is not { } id)
        {
            return;
        }

        // The enriched context's retrying strategy refuses a transaction it did not
        // start, so the pair is handed to it to replay as a unit. They belong
        // together: a retraction without the delete leaves the rule raising more,
        // and a delete without the retraction leaves nudges the owner has cancelled.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE notifications.reminders
                SET state = 'Cancelled', source_recorded_at = {timeProvider.GetUtcNow()}
                WHERE rule_id = {id}
                  AND state = 'Pending'
                """,
                cancellationToken);

            await dbContext.ReminderRules
                .Where(rule => rule.Id == id)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
