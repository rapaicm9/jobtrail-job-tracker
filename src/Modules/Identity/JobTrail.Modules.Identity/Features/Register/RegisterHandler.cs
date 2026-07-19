using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Features.Register;

/// <summary>
/// Opens the account and signs the user straight in - registration hands back
/// the same token pair a login would, so the client never makes two calls.
/// </summary>
internal sealed class RegisterHandler(
    UserManager<ApplicationUser> userManager,
    TokenService tokenService)
{
    public async Task<Result<AuthTokensResponse>> HandleAsync(
        RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            // No separate username concept: the email is the login.
            UserName = request.Email,
            Email = request.Email,
            TimeZoneId = request.TimeZoneId ?? "Etc/UTC",
        };

        var created = await userManager.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            return ToError(created);
        }

        var tokens = await tokenService.IssueAsync(
            UserId.From(user.Id), user.TokenVersion, request.DeviceLabel, cancellationToken);

        return tokens.ToResponse();
    }

    private static Error ToError(IdentityResult result)
    {
        // The unique-email constraint is the truth; surfacing it is a 409, not a
        // validation problem - the request was well-formed, the address is taken.
        if (result.Errors.Any(e =>
                e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                    or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return Error.Conflict("registration.email_taken", "An account with this email already exists.");
        }

        // Anything else is Identity's own validators disagreeing (password
        // policy, email shape). The request validator mirrors those rules, so
        // reaching this path is rare - but map it faithfully when it happens.
        var detail = string.Join(" ", result.Errors.Select(e => e.Description));
        return Error.Validation("registration.invalid", detail);
    }
}
