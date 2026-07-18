using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Domain;

/// <summary>
/// The account aggregate root. ASP.NET Core Identity owns the credentials
/// (email, password hash, security stamp); the columns added here are the
/// domain's own: the user's timezone, a token version for global logout, and a
/// creation timestamp.
/// </summary>
internal sealed class ApplicationUser : IdentityUser<Guid>
{
    // Identity materializes the user before EF inserts it, so the key is minted
    // in code rather than by a DB-side uuidv7() default - still time-ordered.
    public ApplicationUser() => Id = Guid.CreateVersion7();

    /// <summary>IANA timezone (e.g. "Europe/Belgrade"); reminders schedule against it.</summary>
    public string TimeZoneId { get; set; } = "Etc/UTC";

    /// <summary>
    /// Bumped to invalidate every outstanding access token for this user at once
    /// (global logout). Access tokens carry the value they were issued under.
    /// </summary>
    public int TokenVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
