using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// <c>PUT /applications/{id}</c> - replaces the editable fields of the caller's
/// application and returns the fresh state, so no follow-up read is needed. The
/// stage is not touched here; that is the transition endpoint's job.
/// </summary>
internal static class UpdateApplicationEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapPut("/{id:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ApplicationResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateApplicationRequest request,
        IUserContext userContext,
        UpdateApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (UpdateApplicationRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, id, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
