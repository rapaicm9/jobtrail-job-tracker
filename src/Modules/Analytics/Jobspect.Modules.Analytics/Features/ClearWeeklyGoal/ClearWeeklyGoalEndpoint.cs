using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Analytics.Features.ClearWeeklyGoal;

/// <summary>
/// <c>DELETE /analytics/goal</c> - the caller stops tracking a weekly target.
/// <para>
/// <b>Ungated on purpose, and this is the route that would be easiest to get
/// wrong.</b> Gating it would leave a downgraded account holding a goal it could
/// neither change nor be rid of - the trap ADR 0005 draws from the campaign
/// endpoints, where deleting is the only way back to a shape the free tier allows.
/// An account must always be able to reduce itself.
/// </para>
/// </summary>
internal static class ClearWeeklyGoalEndpoint
{
    public static void Map(IEndpointRouteBuilder analytics) =>
        analytics.MapDelete("/goal", HandleAsync).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        ClearWeeklyGoalHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        await handler.ClearAsync(ownerId, cancellationToken);
        return TypedResults.NoContent();
    }
}
