using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.ListContacts;

/// <summary>
/// <c>GET /contacts?applicationId=&amp;companyId=</c> - one page of the caller's own
/// contacts, optionally narrowed to those on a given application or company.
/// Scoped to the token's subject; a user never sees another's. Takes <c>limit</c>
/// and the <c>cursor</c> a previous page returned, alongside any filters.
/// </summary>
internal static class ListContactsEndpoint
{
    public static void Map(IEndpointRouteBuilder contacts) =>
        contacts.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PagedResponse<ContactResponse>>, ProblemHttpResult>> HandleAsync(
        Guid? applicationId,
        Guid? companyId,
        int? limit,
        string? cursor,
        IUserContext userContext,
        ListContactsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (PagingParameters.Validate(limit, cursor, SortKeyKind.Text) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var page = await handler.HandleAsync(
            ownerId, applicationId, companyId, PagingParameters.From(limit, cursor), cancellationToken);
        return TypedResults.Ok(page);
    }
}
