using JobTrail.Modules.Identity.Domain;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// The persistence seam for refresh tokens. It exists so the rotation and
/// reuse-detection logic in <see cref="RefreshTokenService"/> is unit-testable
/// against an in-memory fake - the EF-backed store, and its schema isolation, is
/// exercised by the module's integration tests instead. EF Core remains the
/// "repository"; this is only the narrow surface those two callers share.
/// </summary>
internal interface IRefreshTokenStore
{
    void Add(RefreshToken token);

    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    void Remove(RefreshToken token);

    /// <summary>
    /// Deletes every refresh token the user holds, effective immediately - no
    /// separate <see cref="SaveChangesAsync"/> required. Global logout only.
    /// </summary>
    Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
