using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.UpdateInterview;

/// <summary>
/// <c>PUT /applications/{applicationId}/interviews/{interviewId}</c> - replaces the
/// caller's interview round and returns the fresh state. Another user's round is a 404.
/// </summary>
internal static class UpdateInterviewEndpoint
{
    public static void Map(IEndpointRouteBuilder interviews) =>
        interviews.MapPut("/{interviewId:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<InterviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        Guid interviewId,
        UpdateInterviewRequest request,
        IUserContext userContext,
        UpdateInterviewHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (UpdateInterviewRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, applicationId, interviewId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
