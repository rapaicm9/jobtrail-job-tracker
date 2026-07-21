using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Billing.Contracts;

/// <summary>
/// A user's entitlements have changed - typically the Free → Pro flip on
/// purchase. Carries only the id: a consumer that caches entitlement decisions
/// re-reads through <see cref="IEntitlementQuery"/>, so the new state is never
/// duplicated into the event and can never go stale against the source.
/// </summary>
public sealed record EntitlementChanged(UserId UserId) : IIntegrationEvent;
