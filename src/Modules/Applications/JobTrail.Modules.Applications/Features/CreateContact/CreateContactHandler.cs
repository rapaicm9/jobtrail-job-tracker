using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Features.CreateContact;

/// <summary>
/// Records a contact for the caller. The linked application and company are checked
/// to be the caller's own first, so a bad reference is a clean 422 rather than a
/// foreign-key error. The validator has already ensured at least one link is present.
/// </summary>
internal sealed class CreateContactHandler(ApplicationsDbContext dbContext, ContactLinkGuard linkGuard)
{
    public async Task<Result<ContactResponse>> HandleAsync(
        UserId ownerId, CreateContactRequest request, CancellationToken cancellationToken)
    {
        if (await linkGuard.CheckAsync(ownerId, request.ApplicationId, request.CompanyId, cancellationToken) is { } error)
        {
            return error;
        }

        var contact = new Contact
        {
            OwnerId = ownerId,
            ApplicationId = request.ApplicationId,
            CompanyId = request.CompanyId,
            Name = request.Name!.Trim(),
            Role = ContactFieldMapping.ParseRole(request.Role),
            Email = ApplicationFieldMapping.Clean(request.Email),
            Phone = ApplicationFieldMapping.Clean(request.Phone),
            Notes = ApplicationFieldMapping.Clean(request.Notes),
        };

        dbContext.Contacts.Add(contact);
        await dbContext.SaveChangesAsync(cancellationToken);

        return contact.ToResponse();
    }
}
