using JobTrail.SharedKernel;

namespace JobTrail.Modules.Billing.Contracts;

/// <summary>
/// The one way to ask whether a user is entitled to a capability. The API edge
/// resolves its <c>Feature:*</c> authorization policies through this - never a
/// direct Billing reference, and never a claim trusted from the client, since
/// entitlement is server-side truth.
/// </summary>
public interface IEntitlementQuery
{
    Task<bool> HasEntitlementAsync(UserId userId, Entitlement entitlement, CancellationToken cancellationToken);
}
