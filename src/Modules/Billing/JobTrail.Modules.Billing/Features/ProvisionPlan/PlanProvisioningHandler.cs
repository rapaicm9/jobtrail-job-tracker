using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobTrail.Modules.Billing.Features.ProvisionPlan;

/// <summary>
/// Gives every new account its Free plan, in reaction to <see cref="UserRegistered"/>.
/// <para>
/// Idempotent by leaning on the database rather than a pre-check: the insert is
/// attempted, and a unique-violation on <c>user_id</c> - the mark of an
/// at-least-once redelivery, or a concurrent create - is swallowed, because it
/// means the plan the handler would have made already exists. A pre-read could
/// still race two deliveries into two inserts; the constraint cannot.
/// </para>
/// </summary>
internal sealed class PlanProvisioningHandler(BillingDbContext dbContext)
    : IEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered integrationEvent, CancellationToken cancellationToken)
    {
        dbContext.Plans.Add(new Plan
        {
            UserId = integrationEvent.UserId,
            Tier = PlanTier.Free,
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
