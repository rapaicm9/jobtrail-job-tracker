using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications.Features.ClearReminderRule;

/// <summary>
/// <c>DELETE /reminder-rule</c> - the account stops following up automatically.
/// <para>
/// <b>Ungated on purpose, and this is the route that would be easiest to get
/// wrong.</b> Gating it would leave a downgraded account holding an automation it
/// could neither change nor be rid of, still raising nudges it had no way to stop -
/// the trap ADR 0005 draws from the campaign endpoints. An account must always be
/// able to reduce itself to a shape the free tier allows.
/// </para>
/// </summary>
internal static class ClearReminderRuleEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete("/reminder-rule", HandleAsync).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        ClearReminderRuleHandler handler,
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
