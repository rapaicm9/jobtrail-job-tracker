using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListContacts;

/// <summary>
/// Lists a page of the caller's own contacts, ordered by name. The owner from the
/// token is always the filter; an optional application or company narrows it to
/// that link's contacts. A filter id that isn't the caller's own simply matches
/// nothing - the owner filter still applies - so it never leaks another user's
/// contacts.
/// <para>
/// Paged by cursor on the name and id together, since names repeat. The filters
/// are query parameters the client resends with the cursor rather than something
/// baked into it: a cursor is only a position, and it positions correctly in
/// whatever list it is handed to.
/// </para>
/// </summary>
internal sealed class ListContactsHandler(ApplicationsDbContext dbContext)
{
    public Task<PagedResponse<ContactResponse>> HandleAsync(
        UserId ownerId, Guid? applicationId, Guid? companyId, PageRequest page, CancellationToken cancellationToken)
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

        if (page.Position is { } position)
        {
            // Compared in the database, so the ordering and the page edge agree on
            // one collation - comparing names in memory would not.
            var lastName = position.SortKey;
            var lastId = position.Id;
            query = query.Where(c =>
                string.Compare(c.Name, lastName) > 0 || (c.Name == lastName && c.Id > lastId));
        }

        return PageBuilder.BuildAsync(
            query.OrderBy(c => c.Name).ThenBy(c => c.Id),
            page.Limit,
            c => c.ToResponse(),
            c => new Cursor(c.Id, c.Name),
            cancellationToken);
    }
}
