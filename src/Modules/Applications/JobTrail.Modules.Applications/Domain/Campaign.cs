using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// A named job search an application belongs to. Every account has exactly one
/// default campaign, created with the account and never deleted, so an
/// application always has a campaign to sit in. Pro accounts add more (a later
/// slice); a partial unique index on the default is what keeps "exactly one
/// default per user" true without forbidding the extras. <see cref="OwnerId"/>
/// is a non-FK reference to an Identity account - no cross-schema foreign key, ever.
/// </summary>
internal sealed class Campaign
{
    /// <summary>The name given to every account's auto-created default campaign.</summary>
    public const string DefaultName = "My Applications";

    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The one campaign every user is guaranteed to have: exactly one per user (a
    /// partial unique index enforces it) and not deletable. The extra campaigns a
    /// Pro account creates are not default.
    /// </summary>
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the campaign is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
