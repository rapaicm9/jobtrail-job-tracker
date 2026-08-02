using Jobspect.Modules.Analytics.Features;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Analytics.Features.GetInsights;

/// <summary>
/// <c>GET /analytics/insights?campaignId=</c> - the paid dashboard: the funnel and
/// its rates, how long things took, the weekly trend, and the source and work-mode
/// breakdowns. Scoped to the token's subject.
/// <para>
/// Pro only, and the one place in this module where a gate belongs. It looks like
/// a read and is not: the account's own record stays fully readable at
/// <c>/applications</c> and its headline figures at <c>/analytics/overview</c> -
/// what the paid tier sells is the analysis over them, which is a capability
/// rather than a possession (ADR 0005).
/// </para>
/// <para>
/// Everything arrives together because it all comes from one read of the same
/// rows. The custom-field charts are the deliberate exception: they reach into
/// another module and have to be able to fail on their own (ADR 0017).
/// </para>
/// </summary>
internal static class GetInsightsEndpoint
{
    public static void Map(IEndpointRouteBuilder analytics) =>
        analytics.MapGet("/insights", HandleAsync)
            .RequireAuthorization(FeaturePolicy.For(Entitlement.FullAnalytics));

    private static async Task<Results<Ok<AnalyticsInsightsResponse>, ProblemHttpResult>> HandleAsync(
        Guid? campaignId,
        IUserContext userContext,
        GetInsightsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, campaignId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
