using JobTrail.Modules.Applications.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.EraseData;

/// <summary>
/// The Applications module's reaction to an erasure request: everything it holds
/// about the user, out of its own <c>applications</c> schema. The largest erasure
/// surface in the system, and the one carrying the user's own words - the notes on
/// the activity timeline. Other modules erase their own data from the same event;
/// this handler owns only this module's.
/// <para>
/// Every child is deleted by owner rather than left to cascade from its
/// application. A contact linked only to a company has no application to cascade
/// from, so it would survive - and beyond that one case, an explicit list is this
/// module stating what it holds. A cascade is a silent dependency: change one
/// foreign key to <c>SET NULL</c> later and cascade-based erasure starts leaving
/// rows behind without a word.
/// </para>
/// <para>
/// The order is the one the foreign keys demand - applications before the
/// campaigns they are restricted to - and the whole sequence is one transaction,
/// so this module is never left half-erased. The request is retried until every
/// module's handler succeeds, and each statement here is a set-based delete, so a
/// redelivery simply finds nothing left to do.
/// </para>
/// </summary>
internal sealed class ApplicationsDataErasureHandler(ApplicationsDbContext dbContext)
    : IEventHandler<UserDataDeletionRequested>
{
    public async Task HandleAsync(
        UserDataDeletionRequested integrationEvent, CancellationToken cancellationToken)
    {
        var ownerId = integrationEvent.OwnerId;

        // The retrying execution strategy refuses a transaction it did not start,
        // so the whole sequence is handed to it to replay as one.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            await dbContext.ActivityLog.Where(e => e.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Contacts.Where(c => c.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Interviews.Where(i => i.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Applications.Where(a => a.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Campaigns.Where(c => c.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Companies.Where(c => c.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);

            // The fields the user defined. Their values went with the applications
            // that carried them, so nothing is left pointing here.
            await dbContext.CustomFields.Where(d => d.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);

            // The events still owed on this user's behalf. Undelivered, they would
            // reach consumers after those consumers had erased the same user and
            // put the data back; delivered, they are retained personal data until
            // the pruning window closes. Either way they go with everything else.
            await dbContext.Outbox.Where(m => m.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
