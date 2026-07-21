using JobTrail.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace JobTrail.Modules.Billing.Authorization;

/// <summary>
/// The authorization requirement behind a <c>Feature:*</c> policy: the caller
/// must hold the named <see cref="Entitlement"/>. One requirement type carries
/// every feature, distinguished by which entitlement it names.
/// </summary>
internal sealed class FeatureRequirement(Entitlement entitlement) : IAuthorizationRequirement
{
    public Entitlement Entitlement { get; } = entitlement;
}
