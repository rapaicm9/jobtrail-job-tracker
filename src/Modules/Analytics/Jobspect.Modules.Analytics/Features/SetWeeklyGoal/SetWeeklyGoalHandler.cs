using Jobspect.Modules.Analytics.Persistence;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Analytics.Features.SetWeeklyGoal;

/// <summary>
/// Records the account's weekly target, replacing whatever it was, and answers with
/// the goal as a read of it would report - target, this week, and the progress
/// already made toward it.
/// </summary>
internal sealed class SetWeeklyGoalHandler(
    AnalyticsDbContext dbContext,
    WeeklyGoalReader reader,
    IEntitlementQuery entitlements,
    TimeProvider timeProvider)
{
    public async Task<Result<WeeklyGoalResponse>> HandleAsync(
        UserId ownerId, SetWeeklyGoalRequest request, CancellationToken cancellationToken)
    {
        // The route policy has already refused an unentitled caller, so this never
        // fires through the endpoint. It fires if the endpoint is ever mapped
        // without its policy, or if this handler is called from somewhere that is
        // not that endpoint - the gate belongs to the operation, not to one route.
        if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.FullAnalytics, cancellationToken))
        {
            return AnalyticsErrors.WeeklyGoalNotEntitled;
        }

        // One statement rather than a read followed by an insert or an update. The
        // projections in this module avoid tracked writes because the dispatcher
        // shares a scope across a whole batch, and that reason does not apply here
        // - a request has its own. A different one does: this row is keyed by the
        // caller, so two of their requests arriving together would both find no row
        // and both insert, and one would fail on the key. An upsert has no window
        // between deciding and writing, and PUT of a singleton is an upsert anyway.
        //
        // created_at is left to the column default on insert and untouched on
        // update, so it goes on saying when the account first set itself a goal.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO analytics.weekly_goals (owner_id, target, updated_at)
            VALUES ({ownerId.Value}, {request.Target!.Value}, {timeProvider.GetUtcNow()})
            ON CONFLICT (owner_id) DO UPDATE SET
                target     = excluded.target,
                updated_at = excluded.updated_at
            """,
            cancellationToken);

        return await reader.ReadAsync(ownerId, cancellationToken);
    }
}
