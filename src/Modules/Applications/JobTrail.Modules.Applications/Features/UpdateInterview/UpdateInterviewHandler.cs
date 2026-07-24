using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.UpdateInterview;

/// <summary>
/// Replaces the editable fields of one interview round on the caller's application,
/// including recording its outcome. Ownership and the parent link are the query,
/// so a round on someone else's application - or under a different one - is a 404.
/// </summary>
internal sealed class UpdateInterviewHandler(ApplicationsDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<InterviewResponse>> HandleAsync(
        UserId ownerId,
        Guid applicationId,
        Guid interviewId,
        UpdateInterviewRequest request,
        CancellationToken cancellationToken)
    {
        var interview = await dbContext.Interviews
            .FirstOrDefaultAsync(
                i => i.Id == interviewId && i.ApplicationId == applicationId && i.OwnerId == ownerId,
                cancellationToken);
        if (interview is null)
        {
            return InterviewErrors.NotFound(interviewId);
        }

        interview.ScheduledAt = request.ScheduledAt!.Value;
        interview.Type = InterviewFieldMapping.ParseType(request.Type);
        interview.Format = InterviewFieldMapping.ParseFormat(request.Format);
        interview.Outcome = InterviewFieldMapping.ParseOutcome(request.Outcome);
        interview.Notes = ApplicationFieldMapping.Clean(request.Notes);
        interview.UpdatedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return interview.ToResponse();
    }
}
