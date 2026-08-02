using Jobspect.Infrastructure.Outbox;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;

namespace Jobspect.Modules.Applications.Features.CreateInterview;

/// <summary>
/// Schedules an interview round on one of the caller's applications. The parent
/// application must be the caller's own, or the whole route is a 404 - the
/// application, from the caller's view, doesn't exist. The round starts pending;
/// its outcome is recorded later. The round and the event that will become its
/// reminder commit together: of everything this module announces, a lost interview
/// is the one nobody notices until the interview is missed.
/// </summary>
internal sealed class CreateInterviewHandler(
    ApplicationsDbContext dbContext, OwnershipGuard ownership, TimeProvider timeProvider)
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
            // Generated here rather than by the database, so the round can be named
            // in the event written alongside it - the announcement goes out in the
            // same SaveChanges, before any database-generated id could come back.
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            ApplicationId = applicationId,
            ScheduledAt = request.ScheduledAt!.Value,
            Type = InterviewFieldMapping.ParseType(request.Type),
            Format = InterviewFieldMapping.ParseFormat(request.Format),
            Outcome = InterviewOutcome.Pending,
            Notes = ApplicationFieldMapping.Clean(request.Notes),
        };

        dbContext.Interviews.Add(interview);
        dbContext.Outbox.Add(OutboxMessage.For(
            new InterviewScheduled(
                Guid.CreateVersion7(),
                applicationId,
                interview.Id,
                ownerId,
                interview.ScheduledAt,
                timeProvider.GetUtcNow())));

        await dbContext.SaveChangesAsync(cancellationToken);

        return interview.ToResponse();
    }
}
