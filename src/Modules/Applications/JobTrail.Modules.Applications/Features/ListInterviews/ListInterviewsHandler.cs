using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListInterviews;

/// <summary>
/// Lists the rounds on one of the caller's applications, earliest scheduled first.
/// The parent application is checked to be the caller's own first, so a missing or
/// someone-else's application is a 404 - distinct from an application that exists
/// but has no rounds yet, which is an empty list.
/// <para>Unpaged: an application's rounds are few.</para>
/// </summary>
internal sealed class ListInterviewsHandler(ApplicationsDbContext dbContext, OwnershipGuard ownership)
{
    public async Task<Result<InterviewResponse[]>> HandleAsync(
        UserId ownerId, Guid applicationId, CancellationToken cancellationToken)
    {
        if (!await ownership.OwnsApplicationAsync(ownerId, applicationId, cancellationToken))
        {
            return ApplicationErrors.NotFound(applicationId);
        }

        var interviews = await dbContext.Interviews
            .AsNoTracking()
            .Where(i => i.ApplicationId == applicationId && i.OwnerId == ownerId)
            .OrderBy(i => i.ScheduledAt)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        return interviews.Select(i => i.ToResponse()).ToArray();
    }
}
