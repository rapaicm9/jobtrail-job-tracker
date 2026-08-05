using Jobspect.Infrastructure.Outbox;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features.DeleteCampaign;

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
/// Everything is one transaction, so there is no instant where the campaign is gone
/// and its applications are not yet home, and no instant where they have moved
/// without the move being announced. The restricted foreign key is the backstop for
/// the first: get the order wrong and the delete fails loudly instead of orphaning
/// anything.
/// </para>
/// <para>
/// That transaction is handed to a retrying execution strategy, which means the block
/// inside it has to survive being run twice against one <c>DbContext</c>. The
/// set-based statements do by construction; the outbox rows are the one tracked write
/// in this module that sits inside a retry, and they are built per attempt for that
/// reason - see the comment at the write.
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

        // Which applications are about to move. Only their ids: the move itself is
        // set-based, and this read exists solely because each one owes an event and
        // an event has to name the application it is about.
        var moving = await dbContext.Applications
            .Where(a => a.CampaignId == id && a.OwnerId == ownerId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        // The events, minted once and outside the block that records them: the
        // execution strategy may replay it, and an event id is what a consumer
        // recognizes a redelivery by (ADR 0009), so it has to be the same id each
        // time even though the row carrying it will not be the same row.
        var announcements = moving
            .Select(applicationId => new ApplicationMovedToCampaign(
                Guid.CreateVersion7(), applicationId, ownerId, id, defaultId.Value, now))
            .ToList();

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

            // The rows are built here, on every attempt, and that is the part worth
            // reading twice. A SaveChanges that succeeds under a commit that then
            // fails leaves its entities tracked as Unchanged while the database has
            // rolled them back - and Add is a no-op on anything the change tracker
            // already holds. A replay that reused those instances would therefore
            // re-apply the move and record none of the events announcing it, which is
            // a fact no consumer could ever recover: the outbox prunes, so there is no
            // stream to catch up from. Fresh instances are Detached, so they are
            // tracked and inserted, carrying the ids minted above.
            dbContext.Outbox.AddRange(announcements.Select(announcement => OutboxMessage.For(announcement)));
            await dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        return Result.Success();
    }
}
