using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Features.GetAccount;

/// <summary>
/// Reads the caller's own profile. The lookup is the ownership check: the id
/// comes from the proven token, so a hit is always the caller's row and a miss
/// is a 404 (see <see cref="AccountErrors.NotFound"/>). No CancellationToken -
/// UserManager's read takes none.
/// </summary>
internal sealed class GetAccountHandler(UserManager<ApplicationUser> userManager)
{
    public async Task<Result<AccountResponse>> HandleAsync(UserId userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AccountErrors.NotFound;
        }

        return user.ToResponse();
    }
}
