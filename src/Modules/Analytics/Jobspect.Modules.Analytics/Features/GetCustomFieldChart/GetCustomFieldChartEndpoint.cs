using Jobspect.Modules.Analytics.Features;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Analytics.Features.GetCustomFieldChart;

/// <summary>
/// <c>GET /analytics/custom-fields/{definitionId}?campaignId=</c> - one of the
/// account's own fields, counted. Select fields give a count per option, number
/// fields a five-number summary, date fields a count per month; text and URL
/// fields are not charted and are answered as though they were not there.
/// <para>
/// Its own endpoint rather than a section of the paid dashboard, and that is the
/// design rather than an accident: this panel is served synchronously from another
/// module, so it has to be able to fail without taking the figures beside it with
/// it. A failure here is a 503 for this panel alone.
/// </para>
/// </summary>
internal static class GetCustomFieldChartEndpoint
{
    public static void Map(IEndpointRouteBuilder analytics) =>
        analytics.MapGet("/custom-fields/{definitionId:guid}", HandleAsync)
            .WithName("getCustomFieldChart")
            .RequireAuthorization(FeaturePolicy.For(Entitlement.FullAnalytics));

    private static async Task<Results<Ok<CustomFieldChartResponse>, ProblemHttpResult>> HandleAsync(
        Guid definitionId,
        Guid? campaignId,
        IUserContext userContext,
        GetCustomFieldChartHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, definitionId, campaignId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
