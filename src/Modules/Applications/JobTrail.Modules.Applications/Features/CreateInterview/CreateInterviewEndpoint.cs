using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.CreateInterview;

/// <summary>
/// <c>POST /applications/{applicationId}/interviews</c> - schedules a round on the
/// caller's application and returns it, with a <c>Location</c> pointing at its get
/// route. The owner is the token's subject; the application is from the route.
/// </summary>
internal static class CreateInterviewEndpoint
{
    public static void Map(IEndpointRouteBuilder interviews) =>
        interviews.MapPost("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Created<InterviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        CreateInterviewRequest request,
        IUserContext userContext,
        CreateInterviewHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (CreateInterviewRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, applicationId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created(
                $"/api/v1/applications/{applicationId}/interviews/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
