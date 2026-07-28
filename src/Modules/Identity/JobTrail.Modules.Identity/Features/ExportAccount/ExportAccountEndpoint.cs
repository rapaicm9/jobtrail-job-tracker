using System.Globalization;
using System.Security.Claims;
using JobTrail.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.ExportAccount;

/// <summary>
/// <c>GET /account/export</c> - the caller downloads everything the system holds
/// for their account, as one JSON file. Authenticated, so the account exported is
/// whoever the token proves.
/// <para>
/// Pro. Gating the whole endpoint is right here where it would be wrong elsewhere:
/// an export produces a copy and removes nothing, so an account that cannot export
/// has lost no data and is trapped in nothing. Erasure, which does destroy, stays
/// free and always available - a user must never have to pay to leave.
/// </para>
/// <para>
/// Returned as a file rather than a JSON body: this is something a person saves,
/// and the <c>Content-Disposition</c> that comes with it is what makes a browser
/// treat it that way instead of rendering it.
/// </para>
/// </summary>
internal static class ExportAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder account) =>
        account.MapGet("/export", HandleAsync)
            .RequireAuthorization(FeaturePolicy.For(Entitlement.Export));

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> HandleAsync(
        ClaimsPrincipal principal,
        ExportAccountHandler handler,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!principal.TryGetId(out var userId))
        {
            return CurrentUser.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var today = timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return TypedResults.File(result.Value, "application/json", $"jobtrail-export-{today}.json");
    }
}
