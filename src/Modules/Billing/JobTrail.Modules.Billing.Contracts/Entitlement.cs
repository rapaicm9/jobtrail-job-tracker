namespace JobTrail.Modules.Billing.Contracts;

/// <summary>
/// A capability a plan can unlock - the unit other modules gate their Pro-only
/// features on, named for the feature rather than the tier so a consumer asks
/// "may this user use custom fields?", not "is this user Pro?".
/// <para>
/// In v1 all of these are unlocked together by the one-time Pro purchase; the
/// enum exists so per-feature rules can diverge later without changing callers.
/// </para>
/// </summary>
public enum Entitlement
{
    CustomFields,
    FullAnalytics,
    FollowUpRules,
    MultipleCampaigns,
    Export,
}
