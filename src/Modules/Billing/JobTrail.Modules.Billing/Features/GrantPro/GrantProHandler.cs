using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Billing.Features.GrantPro;

/// <summary>
/// Flips a plan to Pro without a payment - the developer shortcut that stands in
/// for a real purchase while testing gated features. It writes no purchase
/// record: nothing was bought. Otherwise it is the purchase flow's tail, and
/// announces the same <see cref="EntitlementChanged"/>. Already-Pro is an
/// idempotent no-op.
/// </summary>
internal sealed class GrantProHandler(
    BillingDbContext dbContext,
    IEventBus eventBus,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(UserId userId, CancellationToken cancellationToken)
    {
        var plan = await dbContext.Plans.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (plan is null)
        {
            return Error.NotFound("billing.plan_not_found", "This account has no plan to upgrade.");
        }

        if (plan.Tier == PlanTier.Pro)
        {
            return Result.Success();
        }

        plan.Tier = PlanTier.Pro;
        plan.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(new EntitlementChanged(userId), cancellationToken);
        return Result.Success();
    }
}
