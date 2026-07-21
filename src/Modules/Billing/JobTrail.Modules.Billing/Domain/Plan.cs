using JobTrail.SharedKernel;

namespace JobTrail.Modules.Billing.Domain;

/// <summary>
/// A user's entitlement, one row per account. The tier is the truth every
/// <c>Feature:*</c> policy resolves against; a unique constraint on
/// <see cref="UserId"/> makes "exactly one plan per user" the database's job, not
/// a check the code has to remember. <see cref="UserId"/> is a non-FK reference
/// to an Identity account - no cross-schema foreign key, ever.
/// </summary>
internal sealed class Plan
{
    public Guid Id { get; set; }

    public UserId UserId { get; set; }

    public PlanTier Tier { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the tier last changed (e.g. Free → Pro on purchase); null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
