using JobTrail.Modules.Billing.Contracts;
using Shouldly;

namespace JobTrail.Modules.Billing.Tests;

public sealed class FeaturePolicyTests
{
    [Fact]
    public void A_policy_name_is_the_prefix_and_the_entitlement() =>
        FeaturePolicy.For(Entitlement.CustomFields).ShouldBe("Feature:CustomFields");

    [Fact]
    public void Every_entitlement_maps_to_a_distinct_prefixed_policy()
    {
        var names = Enum.GetValues<Entitlement>().Select(FeaturePolicy.For).ToArray();

        names.ShouldAllBe(name => name.StartsWith(FeaturePolicy.Prefix));
        names.Distinct().Count().ShouldBe(names.Length);
    }
}
