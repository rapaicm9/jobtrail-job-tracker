using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.UpdateContact;

/// <summary>
/// Replaces the editable fields of one of the caller's contacts. Ownership is the
/// query, so another user's contact is a 404. The linked application and company
/// are re-checked to be the caller's own before they're attached.
/// </summary>
internal sealed class UpdateContactHandler(
    ApplicationsDbContext dbContext, ContactLinkGuard linkGuard, TimeProvider timeProvider)
{
    public async Task<Result<ContactResponse>> HandleAsync(
        UserId ownerId, Guid id, UpdateContactRequest request, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);
        if (contact is null)
        {
            return ContactErrors.NotFound(id);
        }

        if (await linkGuard.CheckAsync(ownerId, request.ApplicationId, request.CompanyId, cancellationToken) is { } error)
        {
            return error;
        }

        contact.ApplicationId = request.ApplicationId;
        contact.CompanyId = request.CompanyId;
        contact.Name = request.Name!.Trim();
        contact.Role = ContactFieldMapping.ParseRole(request.Role);
        contact.Email = ApplicationFieldMapping.Clean(request.Email);
        contact.Phone = ApplicationFieldMapping.Clean(request.Phone);
        contact.Notes = ApplicationFieldMapping.Clean(request.Notes);
        contact.UpdatedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return contact.ToResponse();
    }
}
