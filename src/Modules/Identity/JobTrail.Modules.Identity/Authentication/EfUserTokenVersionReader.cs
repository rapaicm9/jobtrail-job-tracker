using JobTrail.Modules.Identity.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>EF Core-backed <see cref="IUserTokenVersionReader"/> over the module's own context.</summary>
internal sealed class EfUserTokenVersionReader(IdentityModuleDbContext dbContext) : IUserTokenVersionReader
{
    public async Task<int?> GetTokenVersionAsync(UserId userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(u => u.Id == userId.Value)
            .Select(u => (int?)u.TokenVersion)
            .SingleOrDefaultAsync(cancellationToken);
}
