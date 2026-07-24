using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.ListContacts;

/// <summary>
/// <c>GET /contacts?applicationId=&amp;companyId=</c> - the caller's own contacts,
/// optionally narrowed to those on a given application or company. Scoped to the
/// token's subject; a user never sees another's.
/// </summary>
internal static class ListContactsEndpoint
{
    public static void Map(IEndpointRouteBuilder contacts) =>
        contacts.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<IReadOnlyList<ContactResponse>>, ProblemHttpResult>> HandleAsync(
        Guid? applicationId,
        Guid? companyId,
        IUserContext userContext,
        ListContactsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var contacts = await handler.HandleAsync(ownerId, applicationId, companyId, cancellationToken);
        return TypedResults.Ok(contacts);
    }
}
