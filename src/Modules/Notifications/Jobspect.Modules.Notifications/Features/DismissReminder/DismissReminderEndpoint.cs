using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications.Features.DismissReminder;

/// <summary>
/// <c>POST /reminders/{id}/dismiss</c> - clears one entry and returns it, so a client
/// can update the row it is showing without re-reading the page.
/// <para>
/// An explicit verb rather than a patch of a state field, for the reason the pipeline
/// transition gives: the states a reminder can move between are not a free-form
/// column, and a route that names the move cannot be asked for one that does not
/// exist.
/// </para>
/// </summary>
internal static class DismissReminderEndpoint
{
    public static void Map(IEndpointRouteBuilder reminders) =>
        reminders.MapPost("/{id:guid}/dismiss", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<ReminderResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IUserContext userContext,
        DismissReminderHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, id, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
