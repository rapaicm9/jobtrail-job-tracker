using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.DeleteCampaign;

/// <summary>
/// <c>DELETE /campaigns/{id}</c> - removes one of the caller's campaigns, moving its
/// applications to the default first. Another user's campaign is a 404; the default
/// is a 409.
/// <para>
/// The module's only delete, and the only entity here that can have one. A contact,
/// an interview, a custom field or an application all hold something the user wrote,
/// so they are retired rather than removed; a campaign holds only the grouping, and
/// deleting it costs nothing that cannot be recreated by making another.
/// </para>
/// <para>
/// Not gated, and deliberately: this is how an account that has lost the entitlement
/// returns to a single campaign. Refusing it would leave such an account holding
/// campaigns it can neither use nor be rid of.
/// </para>
/// </summary>
internal static class DeleteCampaignEndpoint
{
    public static void Map(IEndpointRouteBuilder campaigns) =>
        campaigns.MapDelete("/{id:guid}", HandleAsync).RequireAuthorization();

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid id,
        IUserContext userContext,
        DeleteCampaignHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        var result = await handler.HandleAsync(ownerId, id, cancellationToken);
        return result.IsSuccess
            ? TypedResults.NoContent()
            : result.Error.ToProblem();
    }
}
