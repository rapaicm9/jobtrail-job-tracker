using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.ListApplications;

/// <summary>
/// <c>GET /applications</c> - the caller's own applications as list rows. Scoped
/// to the token's subject; a user never sees another's.
/// </summary>
internal static class ListApplicationsEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<IReadOnlyList<ApplicationSummaryResponse>>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        ListApplicationsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var applications = await handler.HandleAsync(ownerId, cancellationToken);
        return TypedResults.Ok(applications);
    }
}
