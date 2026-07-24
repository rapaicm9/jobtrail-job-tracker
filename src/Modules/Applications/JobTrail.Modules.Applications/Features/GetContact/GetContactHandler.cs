using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.GetContact;

/// <summary>
/// Reads one of the caller's contacts. Ownership is the query, so a contact that
/// belongs to someone else is indistinguishable from one that doesn't exist - both
/// a 404, never a 403.
/// </summary>
internal sealed class GetContactHandler(ApplicationsDbContext dbContext)
{
    public async Task<Result<ContactResponse>> HandleAsync(
        UserId ownerId, Guid id, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);

        return contact is null ? ContactErrors.NotFound(id) : contact.ToResponse();
    }
}
