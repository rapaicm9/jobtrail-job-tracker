using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.UpdateAccount;

/// <summary>
/// <c>PUT /account</c> - updates the caller's editable profile (the timezone)
/// and returns the fresh <see cref="AccountResponse"/>, so a client needs no
/// follow-up read. Authenticated, and validated to a field-keyed 422.
/// </summary>
internal static class UpdateAccountEndpoint
{
    // Empty pattern, not "/": see GetAccountEndpoint - keeps the route at a
    // clean PUT /account with no trailing slash.
    public static void Map(IEndpointRouteBuilder account) =>
        account.MapPut("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> HandleAsync(
        UpdateAccountRequest request, ClaimsPrincipal principal, UpdateAccountHandler handler)
    {
        if (!principal.TryGetId(out var userId))
        {
            return CurrentUser.MissingSubject.ToProblem();
        }

        if (UpdateAccountRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(userId, request);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
