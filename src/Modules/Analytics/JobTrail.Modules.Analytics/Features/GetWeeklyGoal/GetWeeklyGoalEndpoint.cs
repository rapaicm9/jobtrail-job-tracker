using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Analytics.Features.GetWeeklyGoal;

/// <summary>
/// <c>GET /analytics/goal</c> - the caller's weekly application target and how far
/// this week has got toward it.
/// <para>
/// <b>Ungated, though setting a goal is Pro.</b> The target is a number the user
/// typed; refusing to hand it back would be the gate becoming a lock on the
/// account's own record, which ADR 0005 forbids - an account that downgrades keeps
/// everything it entered and has to be able to see it. What the entitlement buys is
/// the act of setting a target, and that gate sits on the write.
/// </para>
/// <para>
/// No <c>?campaignId=</c>, unlike the two endpoints beside it. The goal is
/// account-wide, and measuring it against one campaign's slice would report
/// progress toward a target the user never set.
/// </para>
/// </summary>
internal static class GetWeeklyGoalEndpoint
{
    public static void Map(IEndpointRouteBuilder analytics) =>
        analytics.MapGet("/goal", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<WeeklyGoalResponse>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        WeeklyGoalReader reader,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        // Read straight through the shared reader rather than via a handler of this
        // slice's own: retrieving the goal is the whole of what this endpoint does,
        // and the write beside it needs the identical read, so a class here would
        // forward one call and hold nothing.
        return TypedResults.Ok(await reader.ReadAsync(ownerId, cancellationToken));
    }
}
