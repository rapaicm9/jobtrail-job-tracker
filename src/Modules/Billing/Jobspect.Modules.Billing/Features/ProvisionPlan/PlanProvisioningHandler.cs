using Jobspect.Modules.Billing.Domain;
using Jobspect.Modules.Billing.Persistence;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobspect.Modules.Billing.Features.ProvisionPlan;

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
        var plan = dbContext.Plans.Add(new Plan
        {
            UserId = integrationEvent.OwnerId,
            Tier = PlanTier.Free,
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
            // account's plan along with it.
            plan.State = EntityState.Detached;
        }
    }
}
