namespace JobTrail.Modules.Billing.Domain;

/// <summary>
/// The two tiers a user's plan can hold. Free is the generous default every
/// account starts on; Pro is the one-time unlock. Stored as its name, so the
/// column reads for itself and new tiers never depend on ordinal stability.
/// </summary>
internal enum PlanTier
{
    Free,
    Pro,
}
