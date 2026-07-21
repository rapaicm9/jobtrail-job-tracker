using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.DeleteAccount;

/// <summary>
/// <c>DELETE /account</c> - the caller erases their own account. Authenticated,
/// so the account erased is whoever the token proves. Returns 204 once the
/// erasure has been accepted: the deletion fans out through an event and
/// completes shortly after, not within this request.
/// </summary>
internal static class DeleteAccountEndpoint
{
    // Empty pattern, not "/": keeps the route at a clean DELETE /account.
    public static void Map(IEndpointRouteBuilder account) =>
        account.MapDelete("", HandleAsync).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal principal, DeleteAccountHandler handler, CancellationToken cancellationToken)
    {
        if (!principal.TryGetId(out var userId))
        {
            return CurrentUser.MissingSubject.ToProblem();
        }

        await handler.HandleAsync(userId, cancellationToken);
        return TypedResults.NoContent();
    }
}
