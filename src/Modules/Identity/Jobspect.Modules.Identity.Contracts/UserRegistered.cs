using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Identity.Contracts;

/// <summary>
/// A new account has been opened. Other modules react by standing up the per-user
/// state they own - Billing creates the Free plan, Applications the default
/// campaign - none of which Identity knows or names. Carries only the id: a
/// consumer that needs more reads it back through <see cref="IUserProfileQuery"/>,
/// and the account's email stays out of the event stream.
/// <para>
/// Recorded durably, because the state it stands up is state the account cannot
/// function without and cannot ask for again. An account with no plan has no
/// entitlements; an account with no campaign has nowhere to put an application,
/// so every create it attempts fails. Neither module can read Identity's tables to
/// notice the account it was never told about, so a lost announcement is a
/// permanently broken account rather than a delayed one.
/// </para>
/// <para>
/// Delivery is at-least-once, so every handler must be idempotent: standing up
/// state that already exists is a no-op, never an error.
/// </para>
/// </summary>
public sealed record UserRegistered(Guid EventId, UserId OwnerId) : IOutboxEvent
{
    public static string EventType => "identity.user_registered";
}
