using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.UpdateContact;

/// <summary>
/// <c>PUT /contacts/{id}</c> - replaces the editable fields of the caller's contact
/// and returns the fresh state. Another user's contact is a 404.
/// </summary>
internal static class UpdateContactEndpoint
{
    public static void Map(IEndpointRouteBuilder contacts) =>
        contacts.MapPut("/{id:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateContactRequest request,
        IUserContext userContext,
        UpdateContactHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (UpdateContactRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, id, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
