using Jobspect.Infrastructure.Outbox;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features.TransitionApplication;

/// <summary>
/// Moves one of the caller's applications to a new stage. The aggregate owns the
/// rule - <see cref="Application.TransitionTo"/> either applies the move or
/// refuses it as illegal (a 422) - so this handler only loads the right
/// application, asks it to move, and records the change. Ownership is the query,
/// so another user's application is a 404. The accepted move, its activity entry
/// and the events it owes other modules commit together, keeping the timeline
/// honest and the announcement inseparable from the fact.
/// </summary>
internal sealed class TransitionApplicationHandler(ApplicationsDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<ApplicationResponse>> HandleAsync(
        UserId ownerId, Guid id, Stage target, CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(id);
        }

        // One instant, read once: the stamp on the application, the timeline entry
        // and the events all describe the same move and must not disagree about
        // when it happened.
        var now = timeProvider.GetUtcNow();

        var move = application.TransitionTo(target, now);
        if (move.IsFailure)
        {
            return move.Error;
        }

        dbContext.ActivityLog.Add(ActivityLogEntry.ForStageChange(application.Id, ownerId, move.Value));
        Announce(application.Id, ownerId, move.Value, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return application.ToResponse();
    }

    /// <summary>
    /// Records what other modules are owed by this move. Every accepted move is
    /// announced as a stage change; a move that closes an application, or brings a
    /// closed one back, is announced a second time as that fact in its own right -
    /// which stages count as terminal is knowledge that lives here, and a consumer
    /// holding two stage names cannot recover it.
    /// </summary>
    private void Announce(Guid applicationId, UserId ownerId, StageTransition move, DateTimeOffset now)
    {
        var from = move.From.ToString();
        var to = move.To.ToString();

        dbContext.Outbox.Add(OutboxMessage.For(
            new ApplicationStageChanged(Guid.CreateVersion7(), applicationId, ownerId, from, to, now)));

        if (move.Kind is TransitionKind.Terminal or TransitionKind.Reclassify)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new ApplicationReachedTerminal(Guid.CreateVersion7(), applicationId, ownerId, from, to, now)));
        }
        else if (move.Kind is TransitionKind.Reopen)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new ApplicationReopened(Guid.CreateVersion7(), applicationId, ownerId, from, to, now)));
        }
    }
}
