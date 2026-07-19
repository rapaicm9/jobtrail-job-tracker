using JobTrail.Modules.Identity.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.Logout;

/// <summary>
/// Per-device logout: possession of the refresh token is the authority, so the
/// endpoint is anonymous - a client with an expired access token can still log
/// out. Idempotent by design: an unknown token gets the same 204 as a deleted
/// one, revealing nothing about which tokens exist.
/// </summary>
internal static class LogoutEndpoint
{
    public static void Map(IEndpointRouteBuilder identity) =>
        identity.MapPost("/logout", HandleAsync);

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        LogoutRequest request, RefreshTokenService refreshTokenService, CancellationToken cancellationToken)
    {
        if (LogoutRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        await refreshTokenService.RevokeDeviceAsync(request.RefreshToken!, cancellationToken);
        return TypedResults.NoContent();
    }
}
