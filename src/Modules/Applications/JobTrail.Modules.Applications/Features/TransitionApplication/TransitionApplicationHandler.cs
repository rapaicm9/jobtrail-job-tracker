using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.TransitionApplication;

/// <summary>
/// Moves one of the caller's applications to a new stage. The aggregate owns the
/// rule - <see cref="Application.TransitionTo"/> either applies the move or
/// refuses it as illegal (a 422) - so this handler only loads the right
/// application, asks it to move, and records the change. Ownership is the query,
/// so another user's application is a 404. The accepted move and its activity
/// entry commit together, keeping the timeline honest.
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

        var move = application.TransitionTo(target, timeProvider.GetUtcNow());
        if (move.IsFailure)
        {
            return move.Error;
        }

        dbContext.ActivityLog.Add(ActivityLogEntry.ForStageChange(application.Id, ownerId, move.Value));
        await dbContext.SaveChangesAsync(cancellationToken);

        return application.ToResponse();
    }
}
