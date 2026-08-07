using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.GetCustomField;

/// <summary>
/// <c>GET /custom-fields/{id}</c> - reads one of the caller's own custom fields.
/// Another user's field reads as 404.
/// <para>
/// Authenticated but not gated: defining a field is the Pro capability, reading
/// one back is not. An account without the entitlement still has to be able to
/// make sense of the values it recorded while it had it.
/// </para>
/// </summary>
internal static class GetCustomFieldEndpoint
{
    public static void Map(IEndpointRouteBuilder customFields) =>
        customFields.MapGet("/{id:guid}", HandleAsync)
            .WithName("getCustomField")
            .RequireAuthorization();

    private static async Task<Results<Ok<CustomFieldResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IUserContext userContext,
        GetCustomFieldHandler handler,
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
