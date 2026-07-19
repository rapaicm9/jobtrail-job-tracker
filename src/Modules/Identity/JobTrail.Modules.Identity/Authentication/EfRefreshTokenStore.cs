using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>EF Core-backed <see cref="IRefreshTokenStore"/> over the module's own context.</summary>
internal sealed class EfRefreshTokenStore(IdentityModuleDbContext dbContext) : IRefreshTokenStore
{
    public void Add(RefreshToken token) => dbContext.RefreshTokens.Add(token);

    public Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        dbContext.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId)
            .ToListAsync(cancellationToken);

    public void Remove(RefreshToken token) => dbContext.RefreshTokens.Remove(token);

    public async Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            .Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
