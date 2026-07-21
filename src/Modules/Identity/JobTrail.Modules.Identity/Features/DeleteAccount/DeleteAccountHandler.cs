using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Identity.Features.DeleteAccount;

/// <summary>
/// Turns an erasure request into the fact the rest of the system reacts to. It
/// only publishes: the caller's identity was already proven by authentication
/// (a token that outlived its account is rejected before this runs), and the
/// deletions - Identity's own included - happen in the event's handlers, each in
/// its own module and its own transaction.
/// </summary>
internal sealed class DeleteAccountHandler(IEventBus eventBus)
{
    public async Task HandleAsync(UserId userId, CancellationToken cancellationToken) =>
        await eventBus.PublishAsync(new UserDataDeletionRequested(userId), cancellationToken);
}
