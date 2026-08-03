using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications.Features.ListReminders;

/// <summary>
/// <c>GET /reminders</c> - a page of the caller's feed, newest first. Takes
/// <c>limit</c> and the <c>cursor</c> a previous page returned.
/// <para>
/// Ungated. This is the whole of what the reminder engine delivers to a person in
/// this release, and it is core tracking rather than an upgrade - the Pro automation
/// decides what gets <em>raised</em>, never who may read what already was.
/// </para>
/// </summary>
internal static class ListRemindersEndpoint
{
    public static void Map(IEndpointRouteBuilder reminders) =>
        reminders.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PagedResponse<ReminderResponse>>, ProblemHttpResult>> HandleAsync(
        int? limit,
        string? cursor,
        IUserContext userContext,
        ListRemindersHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (PagingParameters.Validate(limit, cursor) is { } errors)
        {
            return Problems.Validation(errors);
        }

        return TypedResults.Ok(
            await handler.HandleAsync(ownerId, PagingParameters.From(limit, cursor), cancellationToken));
    }
}
