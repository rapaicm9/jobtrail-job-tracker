using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobspect.Modules.Applications.Features.ProvisionCampaign;

/// <summary>
/// Gives every new account its default campaign, in reaction to
/// <see cref="UserRegistered"/>, so an application always has a campaign to
/// belong to.
/// <para>
/// Idempotent by leaning on the database rather than a pre-check: the insert is
/// attempted, and a unique-violation on the partial default index - the mark of
/// an at-least-once redelivery, or a concurrent create - is swallowed, because it
/// means the default this handler would have made already exists. A pre-read
/// could still race two deliveries into two defaults; the constraint cannot.
/// </para>
/// </summary>
internal sealed class CampaignProvisioningHandler(ApplicationsDbContext dbContext)
    : IEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered integrationEvent, CancellationToken cancellationToken)
    {
        var campaign = dbContext.Campaigns.Add(new Campaign
        {
            OwnerId = integrationEvent.OwnerId,
            Name = Campaign.DefaultName,
            IsDefault = true,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Already provisioned, so there is nothing to write - but the insert
            // that failed is still tracked, and it has to go. The outbox dispatcher
            // delivers a whole batch in one scope, so this context outlives the
            // delivery: left here, the row would be attempted again by the *next*
            // account's save, fail on the same constraint, and swallow that
            // account's campaign along with it.
            campaign.State = EntityState.Detached;
        }
    }
}
