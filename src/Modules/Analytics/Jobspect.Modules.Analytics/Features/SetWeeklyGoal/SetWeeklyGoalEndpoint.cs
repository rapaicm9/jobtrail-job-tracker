using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Analytics.Features.SetWeeklyGoal;

/// <summary>
/// <c>PUT /analytics/goal</c> - sets or changes how many applications the caller
/// means to send each week, and returns the goal with this week's progress so no
/// follow-up read is needed.
/// <para>
/// Pro only, and the only one of the three goal routes that is. Setting a target is
/// the capability the entitlement pays for; reading it back and clearing it are
/// things an account does with what it already holds, and ADR 0005 keeps both open
/// - deleting especially, since it is the way back to the shape the free tier
/// allows.
/// </para>
/// <para>
/// A full replace rather than a <c>POST</c>: there is one goal per account, sending
/// the same target twice leaves the same state, and the resource has a name of its
/// own before it has a value.
/// </para>
/// </summary>
internal static class SetWeeklyGoalEndpoint
{
    public static void Map(IEndpointRouteBuilder analytics) =>
        analytics.MapPut("/goal", HandleAsync)
            .WithName("setWeeklyGoal")
            .RequireAuthorization(FeaturePolicy.For(Entitlement.FullAnalytics));

    private static async Task<Results<Ok<WeeklyGoalResponse>, ProblemHttpResult>> HandleAsync(
        SetWeeklyGoalRequest request,
        IUserContext userContext,
        SetWeeklyGoalHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (SetWeeklyGoalRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
