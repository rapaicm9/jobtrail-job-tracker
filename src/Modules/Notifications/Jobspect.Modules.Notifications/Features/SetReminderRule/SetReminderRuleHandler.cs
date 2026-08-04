using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.SetReminderRule;

/// <summary>
/// Records the account's follow-up automation, replacing whatever it was, and
/// answers with the rule as a read of it would report.
/// </summary>
internal sealed class SetReminderRuleHandler(
    NotificationsDbContext dbContext,
    IEntitlementQuery entitlements,
    TimeProvider timeProvider)
{
    public async Task<Result<ReminderRuleResponse>> HandleAsync(
        UserId ownerId, SetReminderRuleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The route policy has already refused an unentitled caller, so this never
        // fires through the endpoint. It fires if the endpoint is ever mapped
        // without its policy, or if this handler is called from somewhere that is
        // not that endpoint - the gate belongs to the operation, not to one route.
        if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.FollowUpRules, cancellationToken))
        {
            return ReminderRuleErrors.NotEntitled;
        }

        // One statement rather than a read followed by an insert or an update. This
        // row is keyed by the caller, so two of their requests arriving together
        // would both find no rule and both insert, and one would fail on the unique
        // index that holds the cap. An upsert has no window between deciding and
        // writing, and a PUT of a singleton is an upsert anyway.
        //
        // created_at is left to the column default on insert and untouched on
        // update, so it goes on saying when the account first automated anything.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO notifications.reminder_rules (owner_id, days_after_applied, updated_at)
            VALUES ({ownerId.Value}, {request.DaysAfterApplied!.Value}, {timeProvider.GetUtcNow()})
            ON CONFLICT (owner_id) DO UPDATE SET
                days_after_applied = excluded.days_after_applied,
                updated_at         = excluded.updated_at
            """,
            cancellationToken);

        // Read back rather than returning what was sent: the id and both timestamps
        // are the database's answer, and on an update only one of the three is.
        var rule = await dbContext.ReminderRules
            .AsNoTracking()
            .SingleAsync(candidate => candidate.OwnerId == ownerId, cancellationToken);

        return rule.ToResponse();
    }
}
