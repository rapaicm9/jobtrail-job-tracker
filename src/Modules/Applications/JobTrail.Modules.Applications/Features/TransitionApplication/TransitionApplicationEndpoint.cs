using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.TransitionApplication;

/// <summary>
/// <c>POST /applications/{id}/transition</c> - moves the caller's application to
/// the named stage, returning the updated application. A pipeline move is an
/// explicit action, not a stage field to PATCH, so the state machine gets to
/// judge it: an illegal move is a 422, never a silent write.
/// </summary>
internal static class TransitionApplicationEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapPost("/{id:guid}/transition", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ApplicationResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        TransitionApplicationRequest request,
        IUserContext userContext,
        TransitionApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (TransitionApplicationRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        // Safe after validation: the validator has proven the name parses.
        var target = Enum.Parse<Stage>(request.TargetStage!, ignoreCase: true);

        var result = await handler.HandleAsync(ownerId, id, target, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
