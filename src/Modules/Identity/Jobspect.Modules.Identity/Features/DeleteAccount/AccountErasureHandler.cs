using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Identity.Persistence;
using Jobspect.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Identity.Features.DeleteAccount;

/// <summary>
/// Identity's own reaction to an erasure request: delete the user row, and let
/// the database's cascading foreign keys carry off everything hanging from it -
/// refresh tokens, claims, logins. Other modules erase their own data from the
/// same event; this handler owns only Identity's.
/// <para>
/// A set-based delete rather than <c>UserManager</c>, for two reasons that only
/// matter once delivery is durable. <c>UserManager.DeleteAsync</c> reports failure
/// by returning a result rather than throwing, so a failed erasure would be
/// indistinguishable from a successful one and the event would be marked
/// delivered. And it saves through the same <c>DbContext</c> the dispatcher is
/// using, leaving a failed delete pending on the change tracker for the
/// dispatcher's own save to commit by accident. One statement has neither problem.
/// </para>
/// <para>
/// Idempotent, as at-least-once delivery requires: a request for a user who is
/// already gone deletes nothing and returns quietly.
/// </para>
/// </summary>
internal sealed class AccountErasureHandler(IdentityModuleDbContext dbContext)
    : IEventHandler<UserDataDeletionRequested>
{
    public async Task HandleAsync(
        UserDataDeletionRequested integrationEvent, CancellationToken cancellationToken)
    {
        var userId = integrationEvent.OwnerId.Value;

        await dbContext.Users.Where(user => user.Id == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
