using Jobspect.Modules.Billing.Features;
using Jobspect.Modules.Billing.Features.GetPlan;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Billing.Features.PurchasePro;

/// <summary>
/// <c>POST /billing/purchase</c> - the one-time Pro unlock for the caller. Runs
/// the purchase flow (charge, record, flip, announce) then returns the fresh
/// plan status, so the client sees Pro without a second round-trip. Authenticated
/// and idempotent: a caller already on Pro pays nothing and gets the same 200.
/// Reads the caller through Identity's <see cref="IUserContext"/>.
/// </summary>
internal static class PurchaseProEndpoint
{
    public static void Map(IEndpointRouteBuilder billing) =>
        billing.MapPost("/purchase", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PlanStatusResponse>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        PurchaseProHandler purchase,
        GetPlanHandler getPlan,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.")
                .ToProblem();
        }

        var result = await purchase.HandleAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // Report the plan as it now stands - one shape, read the same way GET does.
        var status = await getPlan.HandleAsync(userId, cancellationToken);
        return status.IsSuccess
            ? TypedResults.Ok(status.Value)
            : status.Error.ToProblem();
    }
}
