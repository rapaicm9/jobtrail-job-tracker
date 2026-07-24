using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.ListApplications;

/// <summary>
/// <c>GET /applications</c> - one page of the caller's own applications as list
/// rows, newest first. Scoped to the token's subject; a user never sees another's.
/// Takes <c>limit</c> and the <c>cursor</c> a previous page returned.
/// </summary>
internal static class ListApplicationsEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PagedResponse<ApplicationSummaryResponse>>, ProblemHttpResult>> HandleAsync(
        int? limit,
        string? cursor,
        IUserContext userContext,
        ListApplicationsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (PagingParameters.Validate(limit, cursor, SortKeyKind.Date) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var page = await handler.HandleAsync(ownerId, PagingParameters.From(limit, cursor), cancellationToken);
        return TypedResults.Ok(page);
    }
}
