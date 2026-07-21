using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace JobTrail.Modules.Billing.Authorization;

/// <summary>
/// Decides a <see cref="FeatureRequirement"/> against the server's entitlement
/// truth: the caller must be a known user who holds the named entitlement. The
/// answer is never taken from a claim the client could forge - it is read fresh
/// through <see cref="IEntitlementQuery"/> every time. An unmet requirement is
/// left unsatisfied, so the policy fails closed.
/// </summary>
internal sealed class FeatureAuthorizationHandler(IUserContext userContext, IEntitlementQuery entitlements)
    : AuthorizationHandler<FeatureRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, FeatureRequirement requirement)
    {
        if (userContext.UserId is not { } userId)
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await entitlements.HasEntitlementAsync(userId, requirement.Entitlement, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
