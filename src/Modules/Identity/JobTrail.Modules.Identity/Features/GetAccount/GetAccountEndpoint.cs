using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.GetAccount;

/// <summary>
/// <c>GET /account</c> - the caller's own profile. Authenticated: the account
/// returned is whoever the token proves, never an id from the request.
/// </summary>
internal static class GetAccountEndpoint
{
    // Empty pattern, not "/": on a "/account" group the latter would map the
    // trailing-slash path and miss a clean GET /account.
    public static void Map(IEndpointRouteBuilder account) =>
        account.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal principal, GetAccountHandler handler)
    {
        if (!principal.TryGetId(out var userId))
        {
            return CurrentUser.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(userId);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
