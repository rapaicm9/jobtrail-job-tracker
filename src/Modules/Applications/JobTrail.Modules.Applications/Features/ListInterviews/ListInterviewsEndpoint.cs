using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.ListInterviews;

/// <summary>
/// <c>GET /applications/{applicationId}/interviews</c> - the rounds on the caller's
/// application, earliest first. A missing or someone-else's application is a 404.
/// </summary>
internal static class ListInterviewsEndpoint
{
    public static void Map(IEndpointRouteBuilder interviews) =>
        interviews.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<InterviewResponse[]>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        IUserContext userContext,
        ListInterviewsHandler handler,
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
