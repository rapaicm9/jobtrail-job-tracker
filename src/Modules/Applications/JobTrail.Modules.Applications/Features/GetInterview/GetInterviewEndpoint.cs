using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.GetInterview;

/// <summary>
/// <c>GET /applications/{applicationId}/interviews/{interviewId}</c> - reads one of
/// the caller's own interview rounds. Another user's round reads as 404.
/// </summary>
internal static class GetInterviewEndpoint
{
    public static void Map(IEndpointRouteBuilder interviews) =>
        interviews.MapGet("/{interviewId:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<InterviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        Guid interviewId,
        IUserContext userContext,
        GetInterviewHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, applicationId, interviewId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
