using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.CreateCampaign;

/// <summary>
/// <c>POST /campaigns</c> - opens another campaign for the authenticated caller and
/// returns it, with a <c>Location</c> pointing at its get route.
/// <para>
/// Pro only, and the only campaign endpoint that is. The entitlement is the right to
/// hold more than one campaign, so it belongs on the act of creating a second - every
/// other endpoint here works on campaigns the account already has, and an account
/// that has lost the entitlement has to be able to read, rename and above all delete
/// them. Deleting is the only way back to a single campaign; gating it would leave
/// such an account stuck in a shape it can no longer reduce.
/// </para>
/// </summary>
internal static class CreateCampaignEndpoint
{
    public static void Map(IEndpointRouteBuilder campaigns) =>
        campaigns.MapPost("", HandleAsync)
            .WithName("createCampaign")
            .RequireAuthorization(FeaturePolicy.For(Entitlement.MultipleCampaigns));

    private static async Task<Results<Created<CampaignResponse>, ProblemHttpResult>> HandleAsync(
        CreateCampaignRequest request,
        IUserContext userContext,
        CreateCampaignHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (CreateCampaignRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/campaigns/{result.Value.Id}", result.Value)
            : result.Error.ToProblem();
    }
}
