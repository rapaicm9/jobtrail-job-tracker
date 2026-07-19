using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Features.Login;

/// <summary>
/// Password login. An unknown email and a wrong password produce the same
/// error, so the endpoint never confirms whether an address has an account.
/// </summary>
internal sealed class LoginHandler(
    UserManager<ApplicationUser> userManager,
    TokenService tokenService)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "The email or password is incorrect.");

    public async Task<Result<AuthTokensResponse>> HandleAsync(
        LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email!);
        if (user is null)
        {
            return InvalidCredentials;
        }

        // CheckPasswordAsync also transparently rehashes on algorithm upgrades.
        if (!await userManager.CheckPasswordAsync(user, request.Password!))
        {
            return InvalidCredentials;
        }

        var tokens = await tokenService.IssueAsync(
            UserId.From(user.Id), user.TokenVersion, request.DeviceLabel, cancellationToken);

        return tokens.ToResponse();
    }
}
