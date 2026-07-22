using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// A single job application - the module's aggregate root, and the thing a user
/// spends the app recording. This slice stands up only its persisted skeleton:
/// identity, ownership and lifecycle timestamps. The pipeline stage and its state
/// machine, the built-in fields, and the campaign and company references arrive in
/// their own slices. <see cref="OwnerId"/> is a non-FK reference to an Identity
/// account - no cross-schema foreign key, ever.
/// </summary>
internal sealed class Application
{
    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the application is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
