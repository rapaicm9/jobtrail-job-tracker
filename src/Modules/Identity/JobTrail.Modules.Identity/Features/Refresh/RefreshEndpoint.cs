using JobTrail.Modules.Identity.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.Refresh;

/// <summary>
/// Rotation is the whole slice, and <see cref="TokenService"/> already owns it -
/// so the endpoint calls the service directly rather than adding a one-line
/// handler class for symmetry's sake.
/// </summary>
internal static class RefreshEndpoint
{
    public static void Map(IEndpointRouteBuilder identity) =>
        identity.MapPost("/refresh", HandleAsync);

    private static async Task<Results<Ok<AuthTokensResponse>, ProblemHttpResult>> HandleAsync(
        RefreshRequest request, TokenService tokenService, CancellationToken cancellationToken)
    {
        if (RefreshRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await tokenService.RefreshAsync(request.RefreshToken!, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value.ToResponse())
            : result.Error.ToProblem();
    }
}
