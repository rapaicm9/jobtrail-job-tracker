using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.CreateContact;

/// <summary>
/// <c>POST /contacts</c> - records a contact for the authenticated caller and
/// returns it, with a <c>Location</c> pointing at its get route. The owner is the
/// token's subject.
/// </summary>
internal static class CreateContactEndpoint
{
    public static void Map(IEndpointRouteBuilder contacts) =>
        contacts.MapPost("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Created<ContactResponse>, ProblemHttpResult>> HandleAsync(
        CreateContactRequest request,
        IUserContext userContext,
        CreateContactHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (CreateContactRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/contacts/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
