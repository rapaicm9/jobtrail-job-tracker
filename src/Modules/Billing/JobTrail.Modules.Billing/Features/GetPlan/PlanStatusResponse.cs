namespace JobTrail.Modules.Billing.Features.GetPlan;

/// <summary>
/// A user's plan as its owner sees it: the tier name and when it last changed.
/// Deliberately narrow - no row id, no owner id - so the read surface can never
/// widen by accident. The plan-status read and the purchase both return this
/// shape, so a client handles one representation.
/// </summary>
internal sealed record PlanStatusResponse(string Tier, DateTimeOffset? UpdatedAt);
