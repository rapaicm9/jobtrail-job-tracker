using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.UpdateCustomField;

/// <summary>
/// <c>PUT /custom-fields/{id}</c> - replaces the editable parts of one of the
/// caller's custom fields. Another user's field is a 404.
/// <para>
/// Pro only, like defining one: changing a field is shaping the paid capability.
/// Reading stays open - see the get and list endpoints.
/// </para>
/// </summary>
internal static class UpdateCustomFieldEndpoint
{
    public static void Map(IEndpointRouteBuilder customFields) =>
        customFields.MapPut("/{id:guid}", HandleAsync)
            .RequireAuthorization(FeaturePolicy.For(Entitlement.CustomFields));

    private static async Task<Results<Ok<CustomFieldResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateCustomFieldRequest request,
        IUserContext userContext,
        UpdateCustomFieldHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (UpdateCustomFieldRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, id, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
