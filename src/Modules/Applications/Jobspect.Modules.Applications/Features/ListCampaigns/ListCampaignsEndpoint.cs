using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.ListCampaigns;

/// <summary>
/// <c>GET /campaigns</c> - every campaign the caller holds, the default flagged. A
/// bare array rather than the paged envelope the other lists use: this is a bounded
/// set a client needs in full to render a picker.
/// <para>
/// Authenticated but not gated, like the single read. A Free account has one
/// campaign and still has to be able to see it.
/// </para>
/// </summary>
internal static class ListCampaignsEndpoint
{
    public static void Map(IEndpointRouteBuilder campaigns) =>
        campaigns.MapGet("", HandleAsync)
            .WithName("listCampaigns")
            .RequireAuthorization();

    private static async Task<Results<Ok<IReadOnlyList<CampaignResponse>>, ProblemHttpResult>> HandleAsync(
        IUserContext userContext,
        ListCampaignsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        return TypedResults.Ok(await handler.HandleAsync(ownerId, cancellationToken));
    }
}
