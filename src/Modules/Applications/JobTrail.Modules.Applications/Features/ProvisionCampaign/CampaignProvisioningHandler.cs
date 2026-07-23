using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobTrail.Modules.Applications.Features.ProvisionCampaign;

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
        dbContext.Campaigns.Add(new Campaign
        {
            OwnerId = integrationEvent.UserId,
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
            // Already provisioned. Nothing to undo: the failed insert never
            // committed, and this scope's context is discarded after the handler.
        }
    }
}
