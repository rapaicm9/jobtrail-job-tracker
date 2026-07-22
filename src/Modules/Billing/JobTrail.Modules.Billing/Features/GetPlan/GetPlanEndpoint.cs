using JobTrail.Modules.Billing.Features;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Billing.Features.GetPlan;

/// <summary>
/// <c>GET /billing/plan</c> - the caller's own plan status. Authenticated: the
/// plan returned is whoever the token proves, never an id from the request.
/// Reads the caller through Identity's <see cref="IUserContext"/> - Billing never
/// parses another module's tokens.
/// </summary>
internal static class GetPlanEndpoint
{
    public static void Map(IEndpointRouteBuilder billing) =>
        billing.MapGet("/plan", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PlanStatusResponse>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext, GetPlanHandler handler, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.")
                .ToProblem();
        }

        var result = await handler.HandleAsync(userId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
