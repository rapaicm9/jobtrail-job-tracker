using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Identity.Contracts;

/// <summary>
/// A user has asked for their account and everything tied to it to be erased.
/// Identity records it; every module that holds data for the user - Identity
/// included - reacts by deleting its own rows. The request is the fact carried
/// here; the deletions are separate reactions, each in its own module.
/// <para>
/// Recorded durably rather than announced in memory, because this is a promise
/// made to the user synchronously and kept afterwards. Lose it between the
/// <c>204</c> and the dispatch and nothing is erased at all - not even Identity's
/// own rows - while the user has already been told it was done.
/// </para>
/// <para>
/// Delivery is at-least-once, so every handler must be idempotent: erasing an
/// already-erased user is a no-op, never an error.
/// </para>
/// </summary>
public sealed record UserDataDeletionRequested(Guid EventId, UserId OwnerId) : IOutboxEvent
{
    public static string EventType => "identity.user_data_deletion_requested";
}
