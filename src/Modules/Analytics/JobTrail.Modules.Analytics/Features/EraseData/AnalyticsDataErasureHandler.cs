using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Features.EraseData;

/// <summary>
/// Analytics' own reaction to an erasure request: take the user's base rows out of
/// the <c>analytics</c> schema. One set-based delete on the owner column, because
/// the base row is the only per-user structure this module has.
/// <para>
/// Idempotent, as at-least-once delivery requires: erasing an already-erased user
/// deletes nothing and returns quietly.
/// </para>
/// <para>
/// <b>It matters that this runs after the Applications module's erasure, and the
/// ordering is arranged in the host</b> (see the registration order in the API's
/// composition root). That module's erasure deletes the events still owed on the
/// user's behalf; if these rows went first, an owed event delivered in the gap
/// would rebuild one for an account that had just been erased. Running last means
/// there is nothing left to deliver by the time this executes.
/// </para>
/// </summary>
internal sealed class AnalyticsDataErasureHandler(AnalyticsDbContext dbContext)
    : IEventHandler<UserDataDeletionRequested>
{
    public async Task HandleAsync(
        UserDataDeletionRequested integrationEvent, CancellationToken cancellationToken)
    {
        var ownerId = integrationEvent.OwnerId;

        await dbContext.ApplicationFacts
            .Where(facts => facts.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
