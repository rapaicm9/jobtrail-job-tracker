using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.LogoutAll;

/// <summary>
/// The one authenticated slice in the group: "log me out everywhere" must come
/// from a proven identity, not from possession of a single device's token.
/// </summary>
internal static class LogoutAllEndpoint
{
    public static void Map(IEndpointRouteBuilder identity) =>
        identity.MapPost("/logout-all", HandleAsync).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal principal, LogoutAllHandler handler, CancellationToken cancellationToken)
    {
        if (!principal.TryGetId(out var userId))
        {
            return CurrentUser.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(userId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.Error.ToProblem();
    }
}
