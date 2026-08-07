using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.CreateCustomField;

/// <summary>
/// <c>POST /custom-fields</c> - defines a custom field for the authenticated
/// caller and returns it, with a <c>Location</c> pointing at its get route.
/// <para>
/// Pro only. Defining a field is the paid capability, so the gate sits here rather
/// than on the group: reading definitions back stays open to everyone, which is
/// what lets an account that has lost the entitlement still make sense of the
/// values it already recorded.
/// </para>
/// </summary>
internal static class CreateCustomFieldEndpoint
{
    public static void Map(IEndpointRouteBuilder customFields) =>
        customFields.MapPost("", HandleAsync)
            .WithName("createCustomField")
            .RequireAuthorization(FeaturePolicy.For(Entitlement.CustomFields));

    private static async Task<Results<Created<CustomFieldResponse>, ProblemHttpResult>> HandleAsync(
        CreateCustomFieldRequest request,
        IUserContext userContext,
        CreateCustomFieldHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (CreateCustomFieldRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/custom-fields/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
