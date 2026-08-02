using Jobspect.SharedKernel;

namespace Jobspect.Modules.Applications.Domain;

/// <summary>
/// A named job search an application belongs to. Every account has exactly one
/// default campaign, created with the account and never deleted, so an
/// application always has a campaign to sit in. Pro accounts add more; a partial
/// unique index on the default is what keeps "exactly one default per user" true
/// without forbidding the extras. <see cref="OwnerId"/> is a non-FK reference to
/// an Identity account - no cross-schema foreign key, ever.
/// <para>
/// A campaign is deletable, unlike a custom field, and the difference is what it
/// holds. Retiring a field would strand the answers given under it, so a field is
/// archived instead; a campaign holds nothing of the user's own - its applications
/// are moved to the default and go on unchanged - so there is nothing left behind
/// to preserve. The default itself is the exception: it is where a delete sends
/// them, so it cannot be the thing deleted.
/// </para>
/// </summary>
internal sealed class Campaign
{
    /// <summary>The name given to every account's auto-created default campaign.</summary>
    public const string DefaultName = "My Applications";

    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    /// <summary>What the user calls this search. Unique among their campaigns.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The name folded to lower case by the database, which is what the uniqueness
    /// index actually compares - two campaigns called "Backend roles" and "backend
    /// roles" are one name to anyone reading a picker. Generated, so nothing can
    /// write it out of step with the name it mirrors.
    /// </summary>
    public string NameNormalized { get; private set; } = string.Empty;

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
