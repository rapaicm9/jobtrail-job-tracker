using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListContacts;

/// <summary>
/// Lists the caller's own contacts, ordered by name. The owner from the token is
/// always the filter; an optional application or company narrows it to that link's
/// contacts. A filter id that isn't the caller's own simply matches nothing - the
/// owner filter still applies - so it never leaks another user's contacts.
/// <para>Unpaged for now; cursor pagination arrives with the other lists next.</para>
/// </summary>
internal sealed class ListContactsHandler(ApplicationsDbContext dbContext)
{
    public async Task<IReadOnlyList<ContactResponse>> HandleAsync(
        UserId ownerId, Guid? applicationId, Guid? companyId, CancellationToken cancellationToken)
    {
        var query = dbContext.Contacts.AsNoTracking().Where(c => c.OwnerId == ownerId);

        if (applicationId is { } appId)
        {
            query = query.Where(c => c.ApplicationId == appId);
        }

        if (companyId is { } companyIdValue)
        {
            query = query.Where(c => c.CompanyId == companyIdValue);
        }

        var contacts = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return [.. contacts.Select(c => c.ToResponse())];
    }
}
