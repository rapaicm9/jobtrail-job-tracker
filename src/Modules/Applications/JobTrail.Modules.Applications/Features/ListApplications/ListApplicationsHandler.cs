using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListApplications;

/// <summary>
/// Lists the caller's own applications, newest application first. The owner from
/// the token is the filter, so a user only ever sees their own. Ordering is by
/// applied date descending, then id (a UUIDv7, so time-ordered) to break ties -
/// two applications sent on the same day are common, and without the tiebreak a
/// page edge could repeat or skip one.
/// <para>
/// Paged by cursor rather than offset: the applied date and id of the last row
/// the client saw become "start after this", so an application added while the
/// client reads doesn't shift the rows underneath it.
/// </para>
/// </summary>
internal sealed class ListApplicationsHandler(ApplicationsDbContext dbContext)
{
    public Task<PagedResponse<ApplicationSummaryResponse>> HandleAsync(
        UserId ownerId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = dbContext.Applications
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId);

        if (page.Position is { } position && SortKeys.ToDate(position.SortKey) is { } appliedDate)
        {
            var lastId = position.Id;
            query = query.Where(a =>
                a.AppliedDate < appliedDate || (a.AppliedDate == appliedDate && a.Id < lastId));
        }

        // Rows map after materialization: the stored stage and work mode are
        // converted enums, so the string projection can't happen in SQL.
        return PageBuilder.BuildAsync(
            query.OrderByDescending(a => a.AppliedDate).ThenByDescending(a => a.Id),
            page.Limit,
            a => a.ToSummary(),
            a => new Cursor(a.Id, SortKeys.From(a.AppliedDate)),
            cancellationToken);
    }
}
