using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Identity.Contracts;

/// <summary>
/// A user has asked for their account and everything tied to it to be erased.
/// Identity publishes it; every module that holds data for the user - Identity
/// included - reacts by deleting its own rows. The request is the fact carried
/// here; the deletions are separate reactions, each in its own module.
/// <para>
/// Delivery is at-least-once, so every handler must be idempotent: erasing an
/// already-erased user is a no-op, never an error.
/// </para>
/// </summary>
public sealed record UserDataDeletionRequested(UserId UserId) : IIntegrationEvent;
