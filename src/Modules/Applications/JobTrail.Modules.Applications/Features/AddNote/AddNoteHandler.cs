using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Features.AddNote;

/// <summary>
/// Adds a note to one of the caller's applications. The application must be the
/// caller's own, or the whole route is a 404 - it doesn't exist, as far as the
/// caller is concerned. The note joins the automatic entries on the same
/// append-only timeline; nothing about the application itself changes, so its
/// <c>UpdatedAt</c> is left alone.
/// </summary>
internal sealed class AddNoteHandler(ApplicationsDbContext dbContext, OwnershipGuard ownership)
{
    public async Task<Result<ActivityEntryResponse>> HandleAsync(
        UserId ownerId, Guid applicationId, AddNoteRequest request, CancellationToken cancellationToken)
    {
        if (!await ownership.OwnsApplicationAsync(ownerId, applicationId, cancellationToken))
        {
            return ApplicationErrors.NotFound(applicationId);
        }

        var entry = ActivityLogEntry.ForNote(applicationId, ownerId, request.Note!.Trim());

        dbContext.ActivityLog.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Id and CreatedAt are database-generated; EF reads them back onto the
        // entity after the insert, so the response is complete without a re-read.
        return entry.ToResponse();
    }
}
