using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel;
using Microsoft.Extensions.Options;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Issues, rotates and revokes refresh tokens per ADR-0003. Rotation replaces the
/// presented token on every use; presenting a token that was already rotated out
/// (a replay) revokes the whole family, on the assumption it was stolen. All
/// failures come back as an <see cref="Error"/> rather than an exception so the
/// caller branches on a value.
/// </summary>
internal sealed class RefreshTokenService(
    IRefreshTokenStore store,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>Starts a new token family (a fresh login on a device).</summary>
    public Task<IssuedRefreshToken> IssueAsync(UserId userId, string? deviceLabel, CancellationToken cancellationToken) =>
        CreateAsync(userId, Guid.CreateVersion7(), deviceLabel, cancellationToken);

    /// <summary>
    /// Validates and rotates a presented refresh token. On success the old token
    /// is retired and a replacement in the same family is returned; a reused,
    /// expired or unknown token fails (and a reused one revokes its family).
    /// </summary>
    public async Task<Result<RotatedRefreshToken>> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenHasher.Hash(rawToken);
        var existing = await store.FindByHashAsync(hash, cancellationToken);

        if (existing is null)
        {
            return Error.Unauthorized("refresh_token.invalid", "The refresh token is not recognized.");
        }

        var now = timeProvider.GetUtcNow();

        if (existing.RevokedAt is not null)
        {
            // A retired token is being replayed - treat as compromise, revoke the family.
            await RevokeFamilyAsync(existing.FamilyId, now, cancellationToken);
            return Error.Unauthorized("refresh_token.reuse_detected", "The refresh token has already been used.");
        }

        if (existing.ExpiresAt <= now)
        {
            existing.RevokedAt = now;
            await store.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized("refresh_token.expired", "The refresh token has expired.");
        }

        // Retire the presented token and mint its replacement in one save (one transaction).
        existing.RevokedAt = now;
        var replacement = await CreateAsync(
            UserId.From(existing.UserId), existing.FamilyId, existing.DeviceLabel, cancellationToken);

        return new RotatedRefreshToken(UserId.From(existing.UserId), replacement.RawToken, replacement.ExpiresAt);
    }

    /// <summary>
    /// Global logout: every refresh token the user holds is deleted. The caller
    /// bumps the account's token version alongside - the bump kills outstanding
    /// access tokens, this kills the refresh tokens that would otherwise mint
    /// new ones at the bumped version.
    /// </summary>
    public Task RevokeAllAsync(UserId userId, CancellationToken cancellationToken) =>
        store.RemoveAllForUserAsync(userId.Value, cancellationToken);

    /// <summary>Per-device logout: the presented token's row is deleted outright.</summary>
    public async Task RevokeDeviceAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = RefreshTokenHasher.Hash(rawToken);
        var existing = await store.FindByHashAsync(hash, cancellationToken);
        if (existing is null)
        {
            return;
        }

        store.Remove(existing);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<IssuedRefreshToken> CreateAsync(
        UserId userId, Guid familyId, string? deviceLabel, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var raw = OpaqueTokenGenerator.Generate();
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId.Value,
            TokenHash = RefreshTokenHasher.Hash(raw),
            FamilyId = familyId,
            DeviceLabel = deviceLabel,
            CreatedAt = now,
            ExpiresAt = now + options.Value.RefreshTokenLifetime,
        };

        store.Add(token);
        await store.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(raw, token.ExpiresAt);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await store.GetFamilyAsync(familyId, cancellationToken);
        foreach (var token in family)
        {
            token.RevokedAt ??= now;
        }

        await store.SaveChangesAsync(cancellationToken);
    }
}
