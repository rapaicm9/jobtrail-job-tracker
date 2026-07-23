using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.SearchCompanies;

/// <summary>
/// Type-ahead over a user's own saved companies. The owner id comes from the
/// proven token, so the query is the ownership boundary - a user only ever sees
/// their own companies. A query under <see cref="MinQueryLength"/> characters
/// matches nothing, so a barely-started search doesn't return half the list, and
/// results are capped at <see cref="MaxResults"/> - the picker needs a shortlist,
/// not everything.
/// </summary>
internal sealed class SearchCompaniesHandler(ApplicationsDbContext dbContext)
{
    private const int MinQueryLength = 3;
    private const int MaxResults = 20;

    public async Task<IReadOnlyList<CompanySummaryResponse>> HandleAsync(
        UserId ownerId, string? query, CancellationToken cancellationToken)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length < MinQueryLength)
        {
            return [];
        }

        // Both sides lower-cased for a case-insensitive match; EF escapes the
        // term's own wildcards when it builds the LIKE.
        var match = term.ToLowerInvariant();

        return await dbContext.Companies
            .Where(c => c.OwnerId == ownerId && c.Name.ToLower().Contains(match))
            .OrderBy(c => c.Name)
            .Take(MaxResults)
            .Select(c => new CompanySummaryResponse(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }
}
