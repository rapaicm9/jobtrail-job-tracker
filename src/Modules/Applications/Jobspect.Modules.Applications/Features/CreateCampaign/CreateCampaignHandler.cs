using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobspect.Modules.Applications.Features.CreateCampaign;

/// <summary>
/// Opens another campaign for the caller. The validator has already settled the
/// name's shape; what is left needs the database - the account's campaign budget,
/// and whether the name is already in use.
/// </summary>
internal sealed class CreateCampaignHandler(ApplicationsDbContext dbContext, IEntitlementQuery entitlements)
{
    public async Task<Result<CampaignResponse>> HandleAsync(
        UserId ownerId, CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        // The route policy has already refused an unentitled caller, so this never
        // fires through the endpoint. It fires if the endpoint is ever mapped
        // without its policy, or if this handler is called from somewhere that is
        // not that endpoint - the gate belongs to the operation, not to one route.
        if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.MultipleCampaigns, cancellationToken))
        {
            return CampaignErrors.NotEntitled;
        }

        var held = await dbContext.Campaigns.CountAsync(c => c.OwnerId == ownerId, cancellationToken);
        if (held >= FieldRules.CampaignsPerOwner)
        {
            return CampaignErrors.LimitReached(FieldRules.CampaignsPerOwner);
        }

        var name = request.Name!.Trim();

        var campaign = new Campaign
        {
            OwnerId = ownerId,
            Name = name,

            // Stated rather than left to the column default: this endpoint only ever
            // adds to the campaign the account already has.
            IsDefault = false,
        };

        dbContext.Campaigns.Add(campaign);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The name index is the only one this insert can violate - the other
            // unique index covers the default rows, and this row is not one.
            return CampaignErrors.NameTaken(name);
        }

        // Nothing can be in it yet; it did not exist a moment ago.
        return campaign.ToResponse(applicationCount: 0);
    }
}
