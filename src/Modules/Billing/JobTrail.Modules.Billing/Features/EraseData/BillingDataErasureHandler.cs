using JobTrail.Modules.Billing.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Billing.Features.EraseData;

/// <summary>
/// Billing's own reaction to an erasure request: delete the user's plan and every
/// purchase behind it, from the module's own <c>billing</c> schema. Other modules
/// erase their own data from the same event; this handler owns only Billing's.
/// <para>
/// Set-based deletes keyed on the non-FK owner id - no rows to load, no schema
/// reached but Billing's. The two statements run outside a shared transaction on
/// purpose: each is idempotent, so an at-least-once redelivery simply re-runs
/// both. Erasing an already-erased user deletes nothing and returns quietly, as
/// the bus requires.
/// </para>
/// </summary>
internal sealed class BillingDataErasureHandler(BillingDbContext dbContext)
    : IEventHandler<UserDataDeletionRequested>
{
    public async Task HandleAsync(
        UserDataDeletionRequested integrationEvent, CancellationToken cancellationToken)
    {
        var userId = integrationEvent.UserId;

        await dbContext.Purchases.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Plans.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
