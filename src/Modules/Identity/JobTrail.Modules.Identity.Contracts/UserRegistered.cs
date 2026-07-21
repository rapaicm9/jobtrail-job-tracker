using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Identity.Contracts;

/// <summary>
/// A new account has been opened. Other modules react by standing up the per-user
/// state they own - Billing creates the Free plan, Applications the default
/// campaign - none of which Identity knows or names. Carries only the id: a
/// consumer that needs more reads it back through <see cref="IUserProfileQuery"/>,
/// and the account's email stays out of the event stream.
/// </summary>
public sealed record UserRegistered(UserId UserId) : IIntegrationEvent;
