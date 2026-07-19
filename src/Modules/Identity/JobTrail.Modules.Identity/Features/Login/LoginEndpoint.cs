using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.Login;

internal static class LoginEndpoint
{
    public static void Map(IEndpointRouteBuilder identity) =>
        identity.MapPost("/login", HandleAsync);

    private static async Task<Results<Ok<AuthTokensResponse>, ProblemHttpResult>> HandleAsync(
        LoginRequest request, LoginHandler handler, CancellationToken cancellationToken)
    {
        if (LoginRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }
}
