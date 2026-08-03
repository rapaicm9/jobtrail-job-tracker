using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications.Features.CountUnreadReminders;

/// <summary>
/// <c>GET /reminders/unread-count</c> - what a client puts on a badge.
/// <para>
/// A route of its own rather than a field on the list envelope. That envelope is a
/// shared wire contract carrying items and a cursor, and growing a member for one
/// list would make it something every other list also has to answer for; this figure
/// is also polled far more often than the feed is read, and it costs one indexed
/// count rather than a page of rows.
/// </para>
/// </summary>
internal static class CountUnreadRemindersEndpoint
{
    public static void Map(IEndpointRouteBuilder reminders) =>
        reminders.MapGet("/unread-count", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<UnreadCountResponse>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        CountUnreadRemindersHandler handler,
        CancellationToken cancellationToken) =>
        userContext.UserId is { } ownerId
            ? TypedResults.Ok(await handler.HandleAsync(ownerId, cancellationToken))
            : Caller.MissingSubject.ToProblem();
}
