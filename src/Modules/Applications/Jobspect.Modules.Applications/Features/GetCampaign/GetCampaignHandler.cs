using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features.GetCampaign;

/// <summary>
/// Reads one of the caller's own campaigns. Ownership is part of the query, so
/// another user's campaign is a 404 rather than a 403 - the difference would tell
/// the caller it exists.
/// </summary>
internal sealed class GetCampaignHandler(ApplicationsDbContext dbContext)
{
    public async Task<Result<CampaignResponse>> HandleAsync(
        UserId ownerId, Guid id, CancellationToken cancellationToken)
    {
        var campaign = await dbContext.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);
        if (campaign is null)
        {
            return CampaignErrors.NotFound(id);
        }

        var applicationCount = await dbContext.Applications
            .CountAsync(a => a.CampaignId == id && a.OwnerId == ownerId, cancellationToken);

        return campaign.ToResponse(applicationCount);
    }
}
