using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.GetActivity;

/// <summary>
/// Reads the whole timeline of one of the caller's applications - the automatic
/// creation and stage-change entries alongside the user's own notes - newest
/// first, which is the order a history is read in. Ties break by id descending (a
/// UUIDv7, so time-ordered): entries written in the same transaction share a
/// timestamp, and the reading must still be stable. The parent application is
/// checked to be the caller's own first, so a missing or someone-else's
/// application is a 404 - distinct from an application whose timeline is somehow
/// empty, which is an empty list.
/// <para>Unpaged: an application's timeline is bounded by hand entry, and cursor
/// pagination arrives with the other list endpoints.</para>
/// </summary>
internal sealed class GetActivityHandler(ApplicationsDbContext dbContext, OwnershipGuard ownership)
{
    public async Task<Result<ActivityEntryResponse[]>> HandleAsync(
        UserId ownerId, Guid applicationId, CancellationToken cancellationToken)
    {
        if (!await ownership.OwnsApplicationAsync(ownerId, applicationId, cancellationToken))
        {
            return ApplicationErrors.NotFound(applicationId);
        }

        var entries = await dbContext.ActivityLog
            .AsNoTracking()
            .Where(e => e.ApplicationId == applicationId && e.OwnerId == ownerId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .ToListAsync(cancellationToken);

        // Map in memory: the stored kind and stages are converted enums, so the
        // string projection is done after materialization, not in SQL.
        return entries.Select(e => e.ToResponse()).ToArray();
    }
}
