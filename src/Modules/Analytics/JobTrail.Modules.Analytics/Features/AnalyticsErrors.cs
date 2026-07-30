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
}
