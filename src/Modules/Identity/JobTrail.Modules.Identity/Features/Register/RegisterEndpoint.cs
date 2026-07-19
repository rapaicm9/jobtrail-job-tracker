using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Identity.Features.Register;

internal static class RegisterEndpoint
{
    public static void Map(IEndpointRouteBuilder identity) =>
        identity.MapPost("/register", HandleAsync);

    private static async Task<Results<Created<AuthTokensResponse>, ProblemHttpResult>> HandleAsync(
        RegisterRequest request, RegisterHandler handler, CancellationToken cancellationToken)
    {
        if (RegisterRequestValidator.Validate(request) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        // No Location header yet: the account resource URL arrives with the
        // /account slice. 201 still marks that something was created.
        return result.IsSuccess
            ? TypedResults.Created((string?)null, result.Value)
            : result.Error.ToProblem();
    }
}
