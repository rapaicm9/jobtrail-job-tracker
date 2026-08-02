using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features.ListCampaigns;

/// <summary>
/// Every campaign the caller holds, oldest first - which puts the default at the
/// top without asking for it, since it was created with the account.
/// <para>
/// Whole, and unpaged. The other collection lists are cursor-paged because they
/// grow without limit; this one is a bounded set a client needs <em>all</em> of to
/// fill a picker, and paging it would mean fetching pages to draw one control. The
/// account's campaign cap is what makes that safe.
/// </para>
/// </summary>
internal sealed class ListCampaignsHandler(ApplicationsDbContext dbContext)
{
    public async Task<IReadOnlyList<CampaignResponse>> HandleAsync(
        UserId ownerId, CancellationToken cancellationToken)
    {
        // The counts come back with the campaigns as one correlated subquery each,
        // rather than a second round trip per row - the cap keeps that a handful.
        var campaigns = await dbContext.Campaigns
            .AsNoTracking()
            .Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new CampaignResponse(
                c.Id,
                c.Name,
                c.IsDefault,
                dbContext.Applications.Count(a => a.CampaignId == c.Id && a.OwnerId == ownerId),
                c.CreatedAt,
                c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return campaigns;
    }
}
