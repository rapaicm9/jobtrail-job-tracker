using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Features.UpdateAccount;

/// <summary>
/// Applies a profile update to the caller's own row. Like the read, the lookup
/// by token subject is the ownership check - a miss is a 404, never a 403. The
/// validator has already vetted the timezone, so the handler trusts the shape
/// and lets a rare store failure surface as a plain failure.
/// </summary>
internal sealed class UpdateAccountHandler(UserManager<ApplicationUser> userManager)
{
    public async Task<Result<AccountResponse>> HandleAsync(UserId userId, UpdateAccountRequest request)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AccountErrors.NotFound;
        }

        user.TimeZoneId = request.TimeZoneId!;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Error.Failure("account.update_failed", "The account could not be updated.");
        }

        return user.ToResponse();
    }
}
