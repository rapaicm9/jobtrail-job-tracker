using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Billing.Domain;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Billing.Persistence;

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
