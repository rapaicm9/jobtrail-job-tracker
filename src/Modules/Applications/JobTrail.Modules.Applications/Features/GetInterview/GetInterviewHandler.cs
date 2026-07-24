using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.GetInterview;

/// <summary>
/// Reads one interview round on the caller's application. Ownership and the
/// parent link are both in the query - it matches on the interview id, the
/// application from the route, and the caller - so a round on someone else's
/// application, or under a different application, is a 404 either way.
/// </summary>
internal sealed class GetInterviewHandler(ApplicationsDbContext dbContext)
{
    public async Task<Result<InterviewResponse>> HandleAsync(
        UserId ownerId, Guid applicationId, Guid interviewId, CancellationToken cancellationToken)
    {
        var interview = await dbContext.Interviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.Id == interviewId && i.ApplicationId == applicationId && i.OwnerId == ownerId,
                cancellationToken);

        return interview is null ? InterviewErrors.NotFound(interviewId) : interview.ToResponse();
    }
}
