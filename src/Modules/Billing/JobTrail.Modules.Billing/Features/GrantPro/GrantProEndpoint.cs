using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Billing.Features.GrantPro;

/// <summary>
/// <c>POST /billing/dev/grant-pro</c> - the developer shortcut that unlocks Pro
/// for the caller without a purchase. Mapped only in Development (the host gates
/// it), and authenticated, so it upgrades whoever the token proves. Reads the
/// caller through Identity's <see cref="IUserContext"/> - Billing never parses
/// another module's tokens.
/// </summary>
internal static class GrantProEndpoint
{
    public static void Map(IEndpointRouteBuilder billing) =>
        billing.MapPost("/grant-pro", HandleAsync).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        IUserContext userContext, GrantProHandler handler, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.")
                .ToProblem();
        }

        var result = await handler.HandleAsync(userId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.Error.ToProblem();
    }
}
