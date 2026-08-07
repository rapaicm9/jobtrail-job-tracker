using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications.Features.SetReminderRule;

/// <summary>
/// <c>PUT /reminder-rule</c> - turns automated follow-ups on, or changes how long
/// they wait.
/// <para>
/// Pro only, and the only one of the three routes here that is. Setting up the
/// automation is the capability the entitlement pays for; reading the rule back and
/// deleting it are things an account does with what it already holds, and ADR 0005
/// keeps both open - deleting especially, since it is the way back to the shape the
/// free tier allows.
/// </para>
/// <para>
/// A full replace rather than a <c>POST</c>, and no id in the path. An account has
/// one rule or none, so the resource has a name of its own before it has a value,
/// and sending the same number twice leaves the same state. It also means the cap of
/// one needs no error to enforce it: there is no request that could ask for a
/// second.
/// </para>
/// </summary>
internal static class SetReminderRuleEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut("/reminder-rule", HandleAsync)
            .WithName("setReminderRule")
            .RequireAuthorization(FeaturePolicy.For(Entitlement.FollowUpRules));

    private static async Task<Results<Ok<ReminderRuleResponse>, ProblemHttpResult>> HandleAsync(
        SetReminderRuleRequest request,
        IUserContext userContext,
        SetReminderRuleHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (SetReminderRuleRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
