using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListInterviews;

/// <summary>
/// Lists a page of the rounds on one of the caller's applications, earliest
/// scheduled first. The parent application is checked to be the caller's own
/// first, so a missing or someone-else's application is a 404 - distinct from an
/// application that exists but has no rounds yet, which is an empty page. That
/// check runs before any paging, so a valid cursor never buys access to an
/// application the caller doesn't own.
/// </summary>
internal sealed class ListInterviewsHandler(ApplicationsDbContext dbContext, OwnershipGuard ownership)
{
    public async Task<Result<PagedResponse<InterviewResponse>>> HandleAsync(
        UserId ownerId, Guid applicationId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await ownership.OwnsApplicationAsync(ownerId, applicationId, cancellationToken))
        {
            return ApplicationErrors.NotFound(applicationId);
        }

        var query = dbContext.Interviews
            .AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.OwnerId == ownerId);

        if (page.Position is { } position && SortKeys.ToInstant(position.SortKey) is { } scheduledAt)
        {
            var lastId = position.Id;
            query = query.Where(i =>
                i.ScheduledAt > scheduledAt || (i.ScheduledAt == scheduledAt && i.Id > lastId));
        }

        return await PageBuilder.BuildAsync(
            query.OrderBy(i => i.ScheduledAt).ThenBy(i => i.Id),
            page.Limit,
            i => i.ToResponse(),
            i => new Cursor(i.Id, SortKeys.From(i.ScheduledAt)),
            cancellationToken);
    }
}
