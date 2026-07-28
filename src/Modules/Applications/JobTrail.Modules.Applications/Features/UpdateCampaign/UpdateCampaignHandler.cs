using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobTrail.Modules.Applications.Features.UpdateCampaign;

/// <summary>
/// Renames one of the caller's campaigns. Ownership is the query, so another user's
/// campaign is a 404. The default is renamed like any other - it is the account's
/// own search and "My Applications" is only where it starts.
/// </summary>
internal sealed class UpdateCampaignHandler(ApplicationsDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<CampaignResponse>> HandleAsync(
        UserId ownerId, Guid id, UpdateCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await dbContext.Campaigns
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);
        if (campaign is null)
        {
            return CampaignErrors.NotFound(id);
        }

        var name = request.Name!.Trim();

        campaign.Name = name;
        campaign.UpdatedAt = timeProvider.GetUtcNow();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return CampaignErrors.NameTaken(name);
        }

        var applicationCount = await dbContext.Applications
            .CountAsync(a => a.CampaignId == id && a.OwnerId == ownerId, cancellationToken);

        return campaign.ToResponse(applicationCount);
    }
}
