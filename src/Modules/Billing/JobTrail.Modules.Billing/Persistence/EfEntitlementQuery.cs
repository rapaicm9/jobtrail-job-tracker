using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Billing.Domain;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Billing.Persistence;

/// <summary>
/// Resolves an entitlement from the user's plan tier. In v1 every capability is
/// unlocked together by Pro, so the tier is the whole answer; the
/// <c>entitlement</c> argument is the seam a per-feature rule would branch on
/// later. A user with no plan (never provisioned) is entitled to nothing.
/// </summary>
internal sealed class EfEntitlementQuery(BillingDbContext dbContext) : IEntitlementQuery
{
    public async Task<bool> HasEntitlementAsync(
        UserId userId, Entitlement entitlement, CancellationToken cancellationToken)
    {
        _ = entitlement;

        var tier = await dbContext.Plans
            .Where(p => p.UserId == userId)
            .Select(p => (PlanTier?)p.Tier)
            .SingleOrDefaultAsync(cancellationToken);

        return tier == PlanTier.Pro;
    }
}
