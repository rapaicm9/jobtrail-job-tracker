using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.GetCampaign;

/// <summary>
/// <c>GET /campaigns/{id}</c> - reads one of the caller's own campaigns. Another
/// user's campaign reads as 404.
/// <para>
/// Authenticated but not gated. Every account has campaigns - a Free one has
/// exactly one - and a client that cannot read them cannot name the campaign an
/// application sits in.
/// </para>
/// </summary>
internal static class GetCampaignEndpoint
{
    public static void Map(IEndpointRouteBuilder campaigns) =>
        campaigns.MapGet("/{id:guid}", HandleAsync)
            .WithName("getCampaign")
            .RequireAuthorization();

    private static async Task<Results<Ok<CampaignResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IUserContext userContext,
        GetCampaignHandler handler,
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
