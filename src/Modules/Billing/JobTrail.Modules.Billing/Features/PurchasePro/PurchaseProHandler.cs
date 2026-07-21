using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Billing.Features.PurchasePro;

/// <summary>
/// The one-time Pro unlock, as one operation: charge, record the purchase, flip
/// the plan to Pro, and announce the change. The charge comes first so a
/// declined payment leaves no trace; the record and the flip commit together so
/// a purchase can never exist without the entitlement it paid for.
/// </summary>
internal sealed class PurchaseProHandler(
    BillingDbContext dbContext,
    IBillingProvider billingProvider,
    IEventBus eventBus,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(UserId userId, CancellationToken cancellationToken)
    {
        var plan = await dbContext.Plans.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (plan is null)
        {
            // Every account is provisioned a Free plan at registration; reaching
            // here means there is nothing to upgrade.
            return Error.NotFound("billing.plan_not_found", "This account has no plan to upgrade.");
        }

        if (plan.Tier == PlanTier.Pro)
        {
            // Already unlocked - don't charge a second time. Idempotent success.
            return Result.Success();
        }

        var payment = await billingProvider.ChargeAsync(userId, cancellationToken);
        if (!payment.Succeeded)
        {
            return Error.Failure("billing.payment_failed", "The payment could not be completed.");
        }

        dbContext.Purchases.Add(new Purchase
        {
            UserId = userId,
            ProviderReference = payment.Reference,
        });

        plan.Tier = PlanTier.Pro;
        plan.UpdatedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(new EntitlementChanged(userId), cancellationToken);
        return Result.Success();
    }
}
