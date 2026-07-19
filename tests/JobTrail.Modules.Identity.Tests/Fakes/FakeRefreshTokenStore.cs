using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;

namespace JobTrail.Modules.Identity.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRefreshTokenStore"/> for unit tests. It holds live
/// references, so a service that mutates a token it read back (setting
/// <c>RevokedAt</c>) is reflected here without any change-tracking machinery -
/// which is exactly the EF behaviour the rotation logic relies on.
/// </summary>
internal sealed class FakeRefreshTokenStore : IRefreshTokenStore
{
    private readonly List<RefreshToken> _tokens = [];

    public IReadOnlyList<RefreshToken> Tokens => _tokens;

    public int SaveCount { get; private set; }

    public void Add(RefreshToken token) => _tokens.Add(token);

    public Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(_tokens.SingleOrDefault(t => t.TokenHash.AsSpan().SequenceEqual(tokenHash)));

    public Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>([.. _tokens.Where(t => t.FamilyId == familyId)]);

    public void Remove(RefreshToken token) => _tokens.Remove(token);

    public Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Immediate, like the EF store's ExecuteDelete - no SaveChanges involved.
        _tokens.RemoveAll(t => t.UserId == userId);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
