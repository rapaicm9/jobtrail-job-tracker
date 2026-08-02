using Jobspect.Modules.Identity.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Identity.Authentication;

/// <summary>EF Core-backed <see cref="IUserTokenVersionReader"/> over the module's own context.</summary>
internal sealed class EfUserTokenVersionReader(IdentityModuleDbContext dbContext) : IUserTokenVersionReader
{
    public async Task<int?> GetTokenVersionAsync(UserId userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(u => u.Id == userId.Value)
            .Select(u => (int?)u.TokenVersion)
            .SingleOrDefaultAsync(cancellationToken);
}
