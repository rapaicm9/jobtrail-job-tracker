using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.AddNote;

/// <summary>
/// <c>POST /applications/{applicationId}/activity</c> - writes a note onto the
/// caller's application timeline and returns the entry it created. The owner is
/// the token's subject; the application is from the route.
/// <para>
/// No <c>Location</c>: a timeline entry has no route of its own - the whole feed
/// is read at once - so there is nothing honest to point at.
/// </para>
/// </summary>
internal static class AddNoteEndpoint
{
    public static void Map(IEndpointRouteBuilder activity) =>
        activity.MapPost("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Created<ActivityEntryResponse>, ProblemHttpResult>> HandleAsync(
        Guid applicationId,
        AddNoteRequest request,
        IUserContext userContext,
        AddNoteHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (AddNoteRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, applicationId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created((string?)null, result.Value)
            : result.Error.ToProblem();
    }
}
