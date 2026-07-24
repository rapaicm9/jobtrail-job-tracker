using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.GetActivity;

/// <summary>
/// Reads a page of one of the caller's application timelines - the automatic
/// creation and stage-change entries alongside the user's own notes - newest
/// first, which is the order a history is read in. Ties break by id descending (a
/// UUIDv7, so time-ordered): entries written in the same transaction share a
/// timestamp, and the reading must still be stable. The parent application is
/// checked to be the caller's own first, so a missing or someone-else's
/// application is a 404 - and that check runs before any paging, so a valid cursor
/// never buys access to a timeline the caller doesn't own.
/// <para>
/// Newest-first paging suits an append-only feed: new entries arrive at the head,
/// so reading further back is never disturbed by what was written meanwhile.
/// </para>
/// </summary>
internal sealed class GetActivityHandler(ApplicationsDbContext dbContext, OwnershipGuard ownership)
{
    public async Task<Result<PagedResponse<ActivityEntryResponse>>> HandleAsync(
        UserId ownerId, Guid applicationId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await ownership.OwnsApplicationAsync(ownerId, applicationId, cancellationToken))
        {
            return ApplicationErrors.NotFound(applicationId);
        }

        var query = dbContext.ActivityLog
            .AsNoTracking()
            .Where(e => e.ApplicationId == applicationId && e.OwnerId == ownerId);

        if (page.Position is { } position && SortKeys.ToInstant(position.SortKey) is { } occurredAt)
        {
            var lastId = position.Id;
            query = query.Where(e =>
                e.CreatedAt < occurredAt || (e.CreatedAt == occurredAt && e.Id < lastId));
        }

        // Rows map after materialization: the stored kind and stages are converted
        // enums, so the string projection can't happen in SQL.
        return await PageBuilder.BuildAsync(
            query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id),
            page.Limit,
            e => e.ToResponse(),
            e => new Cursor(e.Id, SortKeys.From(e.CreatedAt)),
            cancellationToken);
    }
}
