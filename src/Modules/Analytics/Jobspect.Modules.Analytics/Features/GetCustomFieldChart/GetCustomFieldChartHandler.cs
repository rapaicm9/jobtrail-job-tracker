using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Jobspect.Modules.Analytics.Features.GetCustomFieldChart;

/// <summary>
/// The one panel on this dashboard that is not served from this module's own
/// rows.
/// <para>
/// Everything else here is aggregated from the base rows; this asks the
/// Applications module to count its own custom-field answers and hands the result
/// on, storing nothing. That makes it the only figure on the page that cannot lag
/// the event stream - and the only one that can be unavailable while its
/// neighbours are fine.
/// </para>
/// </summary>
internal sealed partial class GetCustomFieldChartHandler(
    ICustomFieldChartQuery charts,
    IEntitlementQuery entitlements,
    ILogger<GetCustomFieldChartHandler> logger)
{
    public async Task<Result<CustomFieldChartResponse>> HandleAsync(
        UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken)
    {
        // The route policy has already refused an unentitled caller, so this never
        // fires through the endpoint. It fires if the endpoint is ever mapped
        // without its policy, or if this handler is called from somewhere that is
        // not that endpoint - the gate belongs to the operation, not to one route.
        if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.FullAnalytics, cancellationToken))
        {
            return AnalyticsErrors.FullAnalyticsNotEntitled;
        }

        CustomFieldChart? chart;

        try
        {
            chart = await charts.GetChartAsync(ownerId, definitionId, campaignId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A deliberate bulkhead rather than loose error handling. This panel
            // depends on another module being reachable, and the one thing it must
            // never do on failure is render as zero - a chart with no bars reads as
            // "you recorded nothing", which is a statement about the user's data
            // that this module is in no position to make.
            ChartUnavailable(definitionId, exception);
            return AnalyticsErrors.ChartUnavailable;
        }

        return chart is null
            ? AnalyticsErrors.ChartNotFound(definitionId)
            : CustomFieldChartResponse.From(chart);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The custom-field chart for definition {DefinitionId} could not be built; "
            + "the panel is reported unavailable.")]
    private partial void ChartUnavailable(Guid definitionId, Exception exception);
}
