using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Features.CreateInterview;

/// <summary>
/// Schedules an interview round on one of the caller's applications. The parent
/// application must be the caller's own, or the whole route is a 404 - the
/// application, from the caller's view, doesn't exist. The round starts pending;
/// its outcome is recorded later.
/// </summary>
internal sealed class CreateInterviewHandler(ApplicationsDbContext dbContext, OwnershipGuard ownership)
{
    public async Task<Result<InterviewResponse>> HandleAsync(
        UserId ownerId, Guid applicationId, CreateInterviewRequest request, CancellationToken cancellationToken)
    {
        if (!await ownership.OwnsApplicationAsync(ownerId, applicationId, cancellationToken))
        {
            return ApplicationErrors.NotFound(applicationId);
        }

        var interview = new Interview
        {
            OwnerId = ownerId,
            ApplicationId = applicationId,
            ScheduledAt = request.ScheduledAt!.Value,
            Type = InterviewFieldMapping.ParseType(request.Type),
            Format = InterviewFieldMapping.ParseFormat(request.Format),
            Outcome = InterviewOutcome.Pending,
            Notes = ApplicationFieldMapping.Clean(request.Notes),
        };

        dbContext.Interviews.Add(interview);
        await dbContext.SaveChangesAsync(cancellationToken);

        return interview.ToResponse();
    }
}
