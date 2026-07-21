using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Authorization;
using JobTrail.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Every capability has its policy wired in the real host: a <c>Feature:*</c>
/// policy that denies anonymous callers and carries the matching entitlement
/// requirement. Proves the registration covers the whole enum, so a new
/// entitlement can never ship without its gate.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FeaturePolicyRegistrationTests(ApiFixture fixture)
{
    public static TheoryData<Entitlement> Entitlements => [.. Enum.GetValues<Entitlement>()];

    [Theory]
    [MemberData(nameof(Entitlements))]
    public async Task Each_entitlement_has_a_deny_anonymous_policy_carrying_its_requirement(Entitlement entitlement)
    {
        using var scope = fixture.CreateScope();
        var policies = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await policies.GetPolicyAsync(FeaturePolicy.For(entitlement));

        policy.ShouldNotBeNull();
        policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().ShouldNotBeEmpty();
        policy.Requirements.OfType<FeatureRequirement>().ShouldHaveSingleItem()
            .Entitlement.ShouldBe(entitlement);
    }
}
