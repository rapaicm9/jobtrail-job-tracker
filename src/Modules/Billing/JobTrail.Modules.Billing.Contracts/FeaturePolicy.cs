namespace JobTrail.Modules.Billing.Contracts;

/// <summary>
/// The names of the authorization policies that gate Pro capabilities. A module
/// protects an endpoint with <c>RequireAuthorization(FeaturePolicy.For(Entitlement.X))</c>
/// and depends on nothing but this name and the <see cref="Entitlement"/> enum -
/// the policy itself, and the entitlement check behind it, stay Billing's.
/// </summary>
public static class FeaturePolicy
{
    public const string Prefix = "Feature:";

    public static string For(Entitlement entitlement) => Prefix + entitlement;
}
