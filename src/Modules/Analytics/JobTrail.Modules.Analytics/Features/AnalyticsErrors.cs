using JobTrail.SharedKernel;

namespace JobTrail.Modules.Analytics.Features;

/// <summary>Failures this module can report, as the kernel errors its endpoints turn into problems.</summary>
internal static class AnalyticsErrors
{
    /// <summary>
    /// The caller's plan does not include the paid figures. A known caller making
    /// an understood request that is not permitted, so a 403 rather than a 404 -
    /// the dashboard exists, it is simply not theirs yet.
    /// </summary>
    public static readonly Error FullAnalyticsNotEntitled = Error.Forbidden(
        "analytics.full_analytics_not_entitled",
        "Full analytics is not included in this account's plan.");

    /// <summary>
    /// The module that holds the custom-field answers could not be asked. Not a
    /// failure of this request and not an empty result: the panel is reported
    /// unavailable so a client can say so, because a chart drawn with no bars
    /// would tell the user they recorded nothing.
    /// </summary>
    public static readonly Error ChartUnavailable = Error.Unavailable(
        "analytics.chart_unavailable",
        "This chart could not be built just now. The rest of the dashboard is unaffected.");

    /// <summary>
    /// No chartable field of that id belongs to the caller. One answer for three
    /// cases - no such definition, somebody else's, or a type that is not charted -
    /// so that asking never confirms an id the caller does not own.
    /// </summary>
    public static Error ChartNotFound(Guid definitionId) => Error.NotFound(
        "analytics.chart_not_found",
        $"No chartable custom field with id {definitionId} belongs to this account.");
}
