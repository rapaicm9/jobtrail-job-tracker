using Jobspect.Modules.Analytics.Features;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Analytics.Features.GetOverview;

/// <summary>
/// <c>GET /analytics/overview?campaignId=</c> - the two figures every account
/// gets: how many applications it has recorded, and the count at each stage.
/// Scoped to the token's subject, so a user only ever sees their own.
/// <para>
/// Both in one response rather than one endpoint each: a dashboard draws them
/// together, they come from a single grouped scan, and splitting them would mean
/// reading the same rows twice to render one panel.
/// </para>
/// <para>
/// Ungated. These are the Free tier's own figures, so there is no
/// <c>Feature:</c> policy here and there should never be one.
/// </para>
/// </summary>
internal static class GetOverviewEndpoint
{
    public static void Map(IEndpointRouteBuilder analytics) =>
        analytics.MapGet("/overview", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<AnalyticsOverviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid? campaignId,
        IUserContext userContext,
        GetOverviewHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        return TypedResults.Ok(await handler.HandleAsync(ownerId, campaignId, cancellationToken));
    }
}
