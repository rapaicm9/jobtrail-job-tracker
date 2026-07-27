using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Persistence;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Features.DeleteAccount;

/// <summary>
/// Turns an erasure request into the fact the rest of the system reacts to. It
/// only records: the caller's identity was already proven by authentication (a
/// token that outlived its account is rejected before this runs), and the
/// deletions - Identity's own included - happen in the event's handlers, each in
/// its own module.
/// <para>
/// The recorded row <em>is</em> the state change here. Everywhere else the outbox
/// carries the announcement of a write that happened beside it; an erasure request
/// changes nothing on its own, so committing the row is what makes the request
/// durable. That is the whole difference between a promise kept and a 204 followed
/// by a restart that erases nothing.
/// </para>
/// </summary>
internal sealed class DeleteAccountHandler(IdentityModuleDbContext dbContext)
{
    public async Task HandleAsync(UserId userId, CancellationToken cancellationToken)
    {
        dbContext.Outbox.Add(OutboxMessage.For(new UserDataDeletionRequested(Guid.CreateVersion7(), userId)));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
