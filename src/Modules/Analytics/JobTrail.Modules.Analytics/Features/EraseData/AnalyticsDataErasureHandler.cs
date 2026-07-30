using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Features.EraseData;

/// <summary>
/// Analytics' own reaction to an erasure request: take everything the user has out
/// of the <c>analytics</c> schema - the base rows built from their events, and the
/// weekly goal they set themselves. One set-based delete on the owner column each.
/// <para>
/// Idempotent, as at-least-once delivery requires: erasing an already-erased user
/// deletes nothing and returns quietly.
/// </para>
/// <para>
/// <b>It matters that this runs after the Applications module's erasure, and the
/// ordering is arranged in the host</b> (see the registration order in the API's
/// composition root). That module's erasure deletes the events still owed on the
/// user's behalf; if the base rows went first, an owed event delivered in the gap
/// would rebuild one for an account that had just been erased. Running last means
/// there is nothing left to deliver by the time this executes.
/// </para>
/// <para>
/// That ordering constrains the base rows only. The goal was authored rather than
/// projected, so no event carries it and nothing can put it back - it would be safe
/// to delete at any point. It goes here because this is where the module gives back
/// what it holds, not because the sequence demands it.
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

        await dbContext.WeeklyGoals
            .Where(goal => goal.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
