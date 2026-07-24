using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.GetActivity;

/// <summary>
/// <c>GET /applications/{applicationId}/activity</c> - the caller's application
/// timeline, newest first. A missing or someone-else's application is a 404.
/// </summary>
internal static class GetActivityEndpoint
{
    public static void Map(IEndpointRouteBuilder activity) =>
        activity.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ActivityEntryResponse[]>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        IUserContext userContext,
        GetActivityHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, applicationId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
