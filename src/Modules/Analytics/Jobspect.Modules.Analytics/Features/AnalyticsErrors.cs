using Jobspect.SharedKernel;

namespace Jobspect.Modules.Analytics.Features;

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
    /// The caller's plan does not include setting a weekly goal. Its own code
    /// rather than the one above, though the entitlement behind both is the same,
    /// because the two refuse different acts and the code is what says which.
    /// <para>
    /// Like its sibling, this is the handler's own re-check and not what a client
    /// normally meets: the route policy refuses an unentitled caller first, with
    /// the framework's bare 403. It is reachable if the endpoint is ever mapped
    /// without its policy, or if the handler is called from somewhere that is not
    /// that endpoint.
    /// </para>
    /// <para>
    /// Only ever raised by the write. Reading a goal the account already set, and
    /// clearing it, are open to every tier - see the endpoints.
    /// </para>
    /// </summary>
    public static readonly Error WeeklyGoalNotEntitled = Error.Forbidden(
        "analytics.weekly_goal_not_entitled",
        "Setting a weekly application goal is not included in this account's plan.");

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
