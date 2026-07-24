using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.GetApplication;

/// <summary>
/// <c>GET /applications/{id}</c> - reads one of the caller's own applications.
/// Scoped to the token's subject; another user's application reads as 404.
/// </summary>
internal static class GetApplicationEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapGet("/{id:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ApplicationResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IUserContext userContext,
        GetApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, id, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
