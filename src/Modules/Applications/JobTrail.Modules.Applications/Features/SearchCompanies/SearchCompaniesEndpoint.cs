using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.SearchCompanies;

/// <summary>
/// <c>GET /companies?query=</c> - type-ahead over the caller's own saved
/// companies, for the create/edit-application picker. Authenticated: the results
/// are scoped to whoever the token proves, never an id from the request. Reads
/// the caller through Identity's <see cref="IUserContext"/> - Applications never
/// parses another module's tokens.
/// </summary>
internal static class SearchCompaniesEndpoint
{
    // Empty pattern, not "/": on a "/companies" group the latter would map the
    // trailing-slash path and miss a clean GET /companies.
    public static void Map(IEndpointRouteBuilder companies) =>
        companies.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<IReadOnlyList<CompanySummaryResponse>>, ProblemHttpResult>> HandleAsync(
        string? query,
        IUserContext userContext,
        SearchCompaniesHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.")
                .ToProblem();
        }

        var companies = await handler.HandleAsync(ownerId, query, cancellationToken);
        return TypedResults.Ok(companies);
    }
}
