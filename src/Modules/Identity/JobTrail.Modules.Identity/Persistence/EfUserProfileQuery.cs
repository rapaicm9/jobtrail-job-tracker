using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Identity.Persistence;

/// <summary>
/// EF Core-backed <see cref="IUserProfileQuery"/> over the module's own context.
/// Projects to the single column asked for, so the boundary read stays as narrow
/// as the contract it serves.
/// </summary>
internal sealed class EfUserProfileQuery(IdentityModuleDbContext dbContext) : IUserProfileQuery
{
    public async Task<string?> GetTimezoneAsync(UserId userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(u => u.Id == userId.Value)
            .Select(u => u.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
}
