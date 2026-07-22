using System.Security.Claims;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Authorization;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The <c>Feature:*</c> authorization decision driven by the real entitlement
/// store: a Pro plan satisfies the requirement, Free and the never-provisioned
/// user do not, and an unauthenticated caller fails closed without a lookup. Only
/// the per-request caller seam (<see cref="IUserContext"/>) is stubbed; the
/// entitlement verdict runs through the real query against the database.
/// <para>
/// The end-to-end HTTP proof (Pro → 200 / Free → 403 / anonymous → 401) waits for
/// the first Feature-gated endpoint - the Applications module's Pro features - no
/// shipping gated route exists to hit yet, and a test-only probe route would
/// exercise scaffolding rather than the real thing.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FeaturePolicyEnforcementTests(ApiFixture fixture)
{
    private static readonly Entitlement Feature = Entitlement.CustomFields;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pro_plan_satisfies_the_feature_requirement()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Pro, Ct);

        (await DecideForAsync(userId)).HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_free_plan_does_not()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Free, Ct);

        (await DecideForAsync(userId)).HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task A_user_with_no_plan_does_not()
    {
        (await DecideForAsync(UserId.New())).HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unauthenticated_caller_fails_closed()
    {
        (await DecideForAsync(userId: null)).HasSucceeded.ShouldBeFalse();
    }

    /// <summary>
    /// Runs the real handler over the real entitlement query for the given caller,
    /// resolving the query from a host scope so it reads the store the app does.
    /// </summary>
    private async Task<AuthorizationHandlerContext> DecideForAsync(UserId? userId)
    {
        using var scope = fixture.CreateScope();
        var entitlements = scope.ServiceProvider.GetRequiredService<IEntitlementQuery>();
        var handler = new FeatureAuthorizationHandler(new StubUserContext(userId), entitlements);

        var requirement = new FeatureRequirement(Feature);
        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity()), resource: null);

        await handler.HandleAsync(context);
        return context;
    }

    private sealed class StubUserContext(UserId? userId) : IUserContext
    {
        public UserId? UserId { get; } = userId;
    }
}
