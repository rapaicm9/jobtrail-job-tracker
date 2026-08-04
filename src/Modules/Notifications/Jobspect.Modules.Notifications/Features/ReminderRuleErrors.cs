using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Features;

/// <summary>Failures raised by the follow-up rule slices.</summary>
internal static class ReminderRuleErrors
{
    /// <summary>
    /// The account has not configured the automation. A 404 rather than an empty
    /// body: the rule is a resource that exists once it is set, and "no rule" is its
    /// absence rather than a rule of nothing.
    /// <para>
    /// It cannot leak anything about another account, unlike the feed's 404 - this
    /// resource is addressed by the caller rather than by an id, so there is no id to
    /// probe with.
    /// </para>
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "reminder_rule.not_found", "No follow-up rule is configured for this account.");

    /// <summary>
    /// The caller tried to set up the automation without the entitlement for it. The
    /// route policy answers this first and a caller will never see it; it exists so
    /// the handler refuses on its own terms rather than trusting that it was only
    /// ever reached through the endpoint that guards it.
    /// </summary>
    public static readonly Error NotEntitled = Error.Forbidden(
        "reminder_rule.not_entitled", "Automated follow-ups require Pro.");
}
