using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications.Features.GetReminderRule;

/// <summary>
/// <c>GET /reminder-rule</c> - the automation this account has configured.
/// <para>
/// <b>Ungated, though setting one is Pro.</b> The number is one the user chose;
/// refusing to hand it back would be the gate becoming a lock on the account's own
/// record, which ADR 0005 forbids. An account that downgrades keeps its rule, keeps
/// receiving nothing new from it once it is deleted, and has to be able to see what
/// it is before deciding.
/// </para>
/// </summary>
internal static class GetReminderRuleEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/reminder-rule", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ReminderRuleResponse>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        GetReminderRuleHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
