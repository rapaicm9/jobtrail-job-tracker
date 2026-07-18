namespace JobTrail.Modules.Identity.Domain;

/// <summary>
/// One row per issued refresh token. The token itself is never stored - only a
/// hash - and rotation replaces the row on every use. Tokens issued from the
/// same login share a <see cref="FamilyId"/>, so detecting a replayed token lets
/// the whole family be revoked at once (ADR-0003).
/// </summary>
internal sealed class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>Owning account. A same-schema FK to <c>ApplicationUser.Id</c>.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the opaque token value. The raw value lives only on the client.</summary>
    public required byte[] TokenHash { get; set; }

    /// <summary>Shared by every token descended from one login, for family-wide revocation.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>Optional human-readable device label ("Pixel 8", "Firefox on Linux").</summary>
    public string? DeviceLabel { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the token is rotated out or explicitly revoked; null while live.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
