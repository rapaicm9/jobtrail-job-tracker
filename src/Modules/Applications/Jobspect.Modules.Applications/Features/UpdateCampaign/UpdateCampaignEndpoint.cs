using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.UpdateCampaign;

/// <summary>
/// <c>PUT /campaigns/{id}</c> - renames one of the caller's campaigns. Another
/// user's campaign is a 404.
/// <para>
/// Not gated: it edits a campaign the account already holds, which is true of every
/// campaign endpoint but the create.
/// </para>
/// </summary>
internal static class UpdateCampaignEndpoint
{
    public static void Map(IEndpointRouteBuilder campaigns) =>
        campaigns.MapPut("/{id:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<CampaignResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        UpdateCampaignRequest request,
        IUserContext userContext,
        UpdateCampaignHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (UpdateCampaignRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(ownerId, id, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
