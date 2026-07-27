using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.GetCustomField;

/// <summary>
/// Reads one of the caller's own custom fields. Ownership is part of the query, so
/// another user's field is a 404 rather than a 403 - the difference would tell the
/// caller it exists.
/// </summary>
internal sealed class GetCustomFieldHandler(ApplicationsDbContext dbContext)
{
    public async Task<Result<CustomFieldResponse>> HandleAsync(
        UserId ownerId, Guid id, CancellationToken cancellationToken)
    {
        var definition = await dbContext.CustomFields
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == ownerId, cancellationToken);

        return definition is null
            ? CustomFieldErrors.NotFound(id)
            : definition.ToResponse();
    }
}
