using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.ListCustomFields;

/// <summary>
/// <c>GET /custom-fields</c> - every custom field the caller has defined, archived
/// ones included and flagged. A bare array rather than the paged envelope the
/// other lists use: this is a bounded set a client needs in full to render a form.
/// <para>
/// Authenticated but not gated, for the same reason as the single read - an
/// account without the entitlement must still be able to interpret the values it
/// already recorded.
/// </para>
/// </summary>
internal static class ListCustomFieldsEndpoint
{
    public static void Map(IEndpointRouteBuilder customFields) =>
        customFields.MapGet("", HandleAsync)
            .WithName("listCustomFields")
            .RequireAuthorization();

    private static async Task<Results<Ok<IReadOnlyList<CustomFieldResponse>>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        ListCustomFieldsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        return TypedResults.Ok(await handler.HandleAsync(ownerId, cancellationToken));
    }
}
