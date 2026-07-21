using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Domain;
using JobTrail.SharedKernel.Events;
using Microsoft.AspNetCore.Identity;

namespace JobTrail.Modules.Identity.Features.DeleteAccount;

/// <summary>
/// Identity's own reaction to an erasure request: delete the user row, and let
/// the database's cascading foreign keys carry off everything hanging from it -
/// refresh tokens, claims, logins. Other modules erase their own data from the
/// same event; this handler owns only Identity's.
/// <para>
/// Idempotent, as the at-least-once bus requires: a request for a user who is
/// already gone finds nothing and returns quietly.
/// </para>
/// </summary>
internal sealed class AccountErasureHandler(UserManager<ApplicationUser> userManager)
    : IEventHandler<UserDataDeletionRequested>
{
    public async Task HandleAsync(
        UserDataDeletionRequested integrationEvent, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(integrationEvent.UserId.ToString());
        if (user is null)
        {
            return;
        }

        await userManager.DeleteAsync(user);
    }
}
