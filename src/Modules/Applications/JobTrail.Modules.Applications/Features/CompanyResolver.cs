using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Turns a create/update request's company inputs into the id the application
/// should reference, honouring the picker's two modes: an existing company chosen
/// by <c>companyId</c>, or a new one typed as <c>companyName</c>. The request
/// validator has already ensured at most one is set.
/// <para>
/// A new company is <em>added but not saved</em> - the calling handler commits it
/// alongside the application and its first activity entry in one transaction, so
/// a company never lingers without the application that introduced it. Resolving a
/// name reuses an exact (case-insensitive) match first, since §3.3 is exact
/// selection with no fuzzy dedup, so "creating" the same company twice references
/// the one row instead of duplicating it.
/// </para>
/// </summary>
internal sealed class CompanyResolver(ApplicationsDbContext dbContext)
{
    public async Task<Result<Guid?>> ResolveAsync(
        UserId ownerId, Guid? companyId, string? companyName, CancellationToken cancellationToken)
    {
        if (companyId is { } id)
        {
            var owned = await dbContext.Companies
                .AnyAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);
            return owned ? (Guid?)id : ApplicationErrors.UnknownCompany(id);
        }

        var name = companyName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return (Guid?)null;
        }

        var match = name.ToLowerInvariant();
        var existingId = await dbContext.Companies
            .Where(c => c.OwnerId == ownerId && c.Name.ToLower() == match)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return existingId;
        }

        // Id generated here so the application can carry the FK in the same
        // SaveChanges; the row is queued, not committed.
        var company = new Company { Id = Guid.CreateVersion7(), OwnerId = ownerId, Name = name };
        dbContext.Companies.Add(company);
        return (Guid?)company.Id;
    }
}
