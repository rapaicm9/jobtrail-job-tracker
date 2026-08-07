using Jobspect.Modules.Billing.Domain;

namespace Jobspect.Modules.Billing.Features.GetPlan;

/// <summary>
/// A user's plan as its owner sees it: the tier and when it last changed.
/// Deliberately narrow - no row id, no owner id - so the read surface can never
/// widen by accident. The plan-status read and the purchase both return this
/// shape, so a client handles one representation.
/// <para>
/// The tier is the enum itself: the host writes it as its name, which is what it
/// has always been on the wire, and holding the type is what lets the described
/// contract say that the two names are the only ones.
/// </para>
/// </summary>
internal sealed record PlanStatusResponse(PlanTier Tier, DateTimeOffset? UpdatedAt);
