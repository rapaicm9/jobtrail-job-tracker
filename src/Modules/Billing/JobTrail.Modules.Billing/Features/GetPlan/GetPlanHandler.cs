using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Billing.Features.GetPlan;

/// <summary>
/// Reads the caller's own plan. The lookup is the ownership check: the id comes
/// from the proven token, so a hit is always the caller's row and a miss is a
/// 404. Every account is provisioned a Free plan at registration, so a miss here
/// means the plan is not in place yet (or the account is gone), never that the
/// user is simply on Free.
/// </summary>
internal sealed class GetPlanHandler(BillingDbContext dbContext)
{
    public async Task<Result<PlanStatusResponse>> HandleAsync(UserId userId, CancellationToken cancellationToken)
    {
        var plan = await dbContext.Plans
            .Where(p => p.UserId == userId)
            .Select(p => new { p.Tier, p.UpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        return plan is null
            ? Error.NotFound("billing.plan_not_found", "This account has no plan.")
            : new PlanStatusResponse(plan.Tier.ToString(), plan.UpdatedAt);
    }
}
