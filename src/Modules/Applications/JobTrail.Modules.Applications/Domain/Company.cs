using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// A company a user applies to - first-class but lightweight: a name and,
/// optionally, a website and free-text notes. Reusable across a user's
/// applications (many applications -> one company), so the client offers it from
/// a type-ahead picker instead of retyping it. <see cref="OwnerId"/> is a non-FK
/// reference to an Identity account - no cross-schema foreign key, ever.
/// </summary>
internal sealed class Company
{
    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    public required string Name { get; set; }

    /// <summary>Optional website; stored only, never fetched.</summary>
    public string? Website { get; set; }

    /// <summary>Optional free-text notes about the company.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the company is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
