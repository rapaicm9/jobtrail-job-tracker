using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Checks that a referenced application or company is the caller's own, before a
/// slice attaches something to it. A reference to another user's resource must be
/// indistinguishable from one that doesn't exist, so these answer a plain "is it
/// yours" - the caller turns a false into whatever failure fits (a request-body
/// reference is a 422; a parent in the route is a 404).
/// </summary>
internal sealed class OwnershipGuard(ApplicationsDbContext dbContext)
{
    public Task<bool> OwnsApplicationAsync(UserId ownerId, Guid applicationId, CancellationToken cancellationToken) =>
        dbContext.Applications.AnyAsync(a => a.Id == applicationId && a.OwnerId == ownerId, cancellationToken);

    public Task<bool> OwnsCompanyAsync(UserId ownerId, Guid companyId, CancellationToken cancellationToken) =>
        dbContext.Companies.AnyAsync(c => c.Id == companyId && c.OwnerId == ownerId, cancellationToken);
}
