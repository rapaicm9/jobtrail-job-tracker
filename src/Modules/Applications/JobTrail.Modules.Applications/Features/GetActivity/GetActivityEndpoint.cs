using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.GetActivity;

/// <summary>
/// <c>GET /applications/{applicationId}/activity</c> - a page of the caller's
/// application timeline, newest first. A missing or someone-else's application is
/// a 404. Takes <c>limit</c> and the <c>cursor</c> a previous page returned.
/// </summary>
internal static class GetActivityEndpoint
{
    public static void Map(IEndpointRouteBuilder activity) =>
        activity.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PagedResponse<ActivityEntryResponse>>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        int? limit,
        string? cursor,
        IUserContext userContext,
        GetActivityHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (PagingParameters.Validate(limit, cursor, SortKeyKind.Instant) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(
            ownerId, applicationId, PagingParameters.From(limit, cursor), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
