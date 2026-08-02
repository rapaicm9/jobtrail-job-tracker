using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.Modules.Billing.Domain;
using Jobspect.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The entitlement seam against the real store: Pro unlocks the capability,
/// Free and the never-provisioned user do not.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EntitlementQueryTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pro_plan_carries_the_entitlement()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Pro, Ct);

        (await HasCustomFields(userId)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_free_plan_does_not()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Free, Ct);

        (await HasCustomFields(userId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_user_with_no_plan_is_entitled_to_nothing()
    {
        (await HasCustomFields(UserId.New())).ShouldBeFalse();
    }

    private async Task<bool> HasCustomFields(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementQuery>();
        return await entitlements.HasEntitlementAsync(userId, Entitlement.CustomFields, Ct);
    }
}
