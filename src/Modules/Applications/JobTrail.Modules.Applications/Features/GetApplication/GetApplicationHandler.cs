using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.GetApplication;

/// <summary>
/// Reads one of the caller's applications. Ownership is the query - it filters on
/// the owner from the token - so an id that belongs to someone else is
/// indistinguishable from one that doesn't exist: both return
/// <see cref="ApplicationErrors.NotFound"/>, a 404, never a 403.
/// </summary>
internal sealed class GetApplicationHandler(ApplicationsDbContext dbContext)
{
    public async Task<Result<ApplicationResponse>> HandleAsync(
        UserId ownerId, Guid id, CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId, cancellationToken);

        return application is null ? ApplicationErrors.NotFound(id) : application.ToResponse();
    }
}
