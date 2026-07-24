using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.GetContact;

/// <summary>
/// <c>GET /contacts/{id}</c> - reads one of the caller's own contacts. Another
/// user's contact reads as 404.
/// </summary>
internal static class GetContactEndpoint
{
    public static void Map(IEndpointRouteBuilder contacts) =>
        contacts.MapGet("/{id:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IUserContext userContext,
        GetContactHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, id, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
