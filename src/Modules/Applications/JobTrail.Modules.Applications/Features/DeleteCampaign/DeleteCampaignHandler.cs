using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.DeleteCampaign;

/// <summary>
/// Removes one of the caller's campaigns and sends everything in it to the default.
/// Ownership is the query, so another user's campaign is a 404.
/// <para>
/// The applications survive the campaign, which is the whole reason a campaign may
/// be deleted at all: nothing the user wrote is lost, only the folder it sat in.
/// They are reassigned rather than cascaded or nulled out - an application must
/// belong to a campaign - and the default is the one campaign guaranteed to be there
/// to receive them, which is also why the default itself cannot be deleted.
/// </para>
/// <para>
/// Both statements are one transaction, so there is no instant where the campaign is
/// gone and its applications are not yet home. The restricted foreign key is the
/// backstop for exactly that: get the order wrong and the delete fails loudly
/// instead of orphaning anything.
/// </para>
/// </summary>
internal sealed class DeleteCampaignHandler(ApplicationsDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(UserId ownerId, Guid id, CancellationToken cancellationToken)
    {
        // Projected rather than loaded: the row is about to be deleted by a
        // set-based statement, and a tracked copy of it would only be something to
        // remember not to save.
        var isDefault = await dbContext.Campaigns
            .Where(c => c.Id == id && c.OwnerId == ownerId)
            .Select(c => (bool?)c.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);
        if (isDefault is null)
        {
            return CampaignErrors.NotFound(id);
        }

        if (isDefault.Value)
        {
            return CampaignErrors.DefaultNotDeletable;
        }

        var defaultId = await dbContext.Campaigns
            .Where(c => c.OwnerId == ownerId && c.IsDefault)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (defaultId is null)
        {
            return ApplicationErrors.NoDefaultCampaign;
        }

        var now = timeProvider.GetUtcNow();

        // The retrying execution strategy refuses a transaction it did not start, so
        // the whole sequence is handed to it to replay as one.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Set-based, because this is a move of every row in the campaign and
            // there is nothing to be gained by materializing them. UpdatedAt is
            // stamped in the same statement: these applications did change, and a
            // client holding a copy has no other way to notice.
            await dbContext.Applications
                .Where(a => a.CampaignId == id && a.OwnerId == ownerId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(a => a.CampaignId, defaultId.Value)
                        .SetProperty(a => a.UpdatedAt, (DateTimeOffset?)now),
                    cancellationToken);

            await dbContext.Campaigns
                .Where(c => c.Id == id && c.OwnerId == ownerId)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        return Result.Success();
    }
}
