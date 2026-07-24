using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.CreateApplication;

/// <summary>
/// <c>POST /applications</c> - opens a new application for the authenticated
/// caller and returns it, with a <c>Location</c> pointing at its get route. The
/// owner is whoever the token proves, never a value from the body.
/// </summary>
internal static class CreateApplicationEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapPost("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Created<ApplicationResponse>, ProblemHttpResult>> HandleAsync(
        CreateApplicationRequest request,
        IUserContext userContext,
        CreateApplicationHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (CreateApplicationRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/applications/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
