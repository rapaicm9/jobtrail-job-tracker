using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListCustomFields;

/// <summary>
/// Every custom field the caller has defined, in the order they defined them -
/// which is the order a form should offer them in.
/// <para>
/// Whole, and unpaged. The other collection lists are cursor-paged because they
/// grow without limit; this one is a bounded set a client needs <em>all</em> of to
/// draw a single application form, and paging it would mean fetching pages to
/// render one page. The account's field cap is what makes that safe.
/// </para>
/// <para>
/// Archived fields are included, flagged rather than hidden. A client rendering an
/// existing application still has to label the values recorded against a field
/// that has since been retired; one that is offering a blank form filters them out.
/// </para>
/// </summary>
internal sealed class ListCustomFieldsHandler(ApplicationsDbContext dbContext)
{
    public async Task<IReadOnlyList<CustomFieldResponse>> HandleAsync(
        UserId ownerId, CancellationToken cancellationToken)
    {
        // Mapped after materializing: ToResponse is ours, not something the
        // provider could translate into SQL.
        var definitions = await dbContext.CustomFields
            .AsNoTracking()
            .Where(d => d.OwnerId == ownerId)
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

        return [.. definitions.Select(definition => definition.ToResponse())];
    }
}
