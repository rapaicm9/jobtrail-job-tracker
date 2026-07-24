using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListApplications;

/// <summary>
/// Lists the caller's own applications, newest application first. The owner from
/// the token is the filter, so a user only ever sees their own. Ordering is by
/// applied date descending, then id (a UUIDv7, so time-ordered) to break ties -
/// deterministic, which matters once this grows a cursor.
/// <para>
/// This returns the whole list for now; cursor-based pagination and a consistent
/// envelope arrive with the other list endpoints in the next sprint. A user's
/// application count is bounded by hand entry, so an unpaged read is safe in the
/// meantime.
/// </para>
/// </summary>
internal sealed class ListApplicationsHandler(ApplicationsDbContext dbContext)
{
    public async Task<IReadOnlyList<ApplicationSummaryResponse>> HandleAsync(
        UserId ownerId, CancellationToken cancellationToken)
    {
        var applications = await dbContext.Applications
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId)
            .OrderByDescending(a => a.AppliedDate)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

        // Map in memory: the stored stage/work-mode are converted enums, so the
        // string projection is done after materialization, not in SQL.
        return [.. applications.Select(a => a.ToSummary())];
    }
}
