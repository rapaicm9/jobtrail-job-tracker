using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Applications.Contracts;
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
/// An edit that changes whether, or when, the round is still awaited is announced,
/// since the reminders standing against it are held by a module that cannot see
/// the edit.
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

        // How the round stood before the replace, to compare against after it.
        var wasAwaited = IsAwaited(interview);
        var previousScheduledAt = interview.ScheduledAt;

        interview.ScheduledAt = request.ScheduledAt!.Value;
        interview.Type = InterviewFieldMapping.ParseType(request.Type);
        interview.Format = InterviewFieldMapping.ParseFormat(request.Format);
        interview.Outcome = InterviewFieldMapping.ParseOutcome(request.Outcome);
        interview.Notes = ApplicationFieldMapping.Clean(request.Notes);

        var now = timeProvider.GetUtcNow();
        interview.UpdatedAt = now;

        Announce(interview, wasAwaited, previousScheduledAt, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return interview.ToResponse();
    }

    /// <summary>
    /// A round still waiting on its outcome is one somebody should be reminded
    /// about. That is the whole of what the reminder-holders care about, so it is
    /// what this handler compares across the edit.
    /// </summary>
    private static bool IsAwaited(Interview interview) => interview.Outcome is InterviewOutcome.Pending;

    /// <summary>
    /// Records what this edit owes the modules holding reminders for the round.
    /// A round that is awaited and either newly so or moved needs a reminder at its
    /// instant; a round that was awaited and has been called off needs its reminders
    /// dropped. Recording that a round happened, pass or fail, needs neither: the
    /// instant it was armed for is already in the past by the time anyone types the
    /// outcome in.
    /// </summary>
    private void Announce(
        Interview interview, bool wasAwaited, DateTimeOffset previousScheduledAt, DateTimeOffset now)
    {
        if (IsAwaited(interview) && (!wasAwaited || interview.ScheduledAt != previousScheduledAt))
        {
            // Newly awaited - a cancelled round put back on - or awaited at a new
            // time. Either way it carries the id the reminders were armed under, so
            // a consumer replaces what it holds rather than adding to it.
            dbContext.Outbox.Add(OutboxMessage.For(
                new InterviewScheduled(
                    Guid.CreateVersion7(),
                    interview.ApplicationId,
                    interview.Id,
                    interview.OwnerId,
                    interview.ScheduledAt,
                    now)));
        }
        else if (wasAwaited && interview.Outcome is InterviewOutcome.Cancelled)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new InterviewCancelled(
                    Guid.CreateVersion7(),
                    interview.ApplicationId,
                    interview.Id,
                    interview.OwnerId,
                    now)));
        }
    }
}
