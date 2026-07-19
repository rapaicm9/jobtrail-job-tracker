using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Features.LogoutAll;

/// <summary>
/// Global logout, in two strokes: bump the token version (kills every
/// outstanding access token at its next check), then delete the user's refresh
/// tokens (kills the sessions that would otherwise mint new access tokens at
/// the bumped version). In that order - if the second stroke fails, the access
/// tokens are already dead and a retry finishes the job.
/// </summary>
internal sealed class LogoutAllHandler(
    UserManager<ApplicationUser> userManager,
    RefreshTokenService refreshTokenService)
{
    public async Task<Result> HandleAsync(UserId userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.Unauthorized("auth.user_not_found", "The account for this token no longer exists.");
        }

        user.TokenVersion++;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Error.Failure("auth.logout_all_failed", "The global logout could not be completed.");
        }

        await refreshTokenService.RevokeAllAsync(userId, cancellationToken);
        return Result.Success();
    }
}
