using System.Security.Claims;
using JobTrail.Modules.Billing.Authorization;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Shouldly;

namespace JobTrail.Modules.Billing.Tests;

public sealed class FeatureAuthorizationHandlerTests
{
    private static readonly Entitlement Feature = Entitlement.CustomFields;

    [Fact]
    public async Task An_entitled_user_satisfies_the_requirement()
    {
        var context = await DecideAsync(userId: UserId.New(), holdsEntitlement: true);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_user_without_the_entitlement_does_not()
    {
        var context = await DecideAsync(userId: UserId.New(), holdsEntitlement: false);

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task No_authenticated_user_fails_closed_without_asking_billing()
    {
        var entitlements = new RecordingEntitlementQuery(result: true);
        var context = await DecideAsync(userId: null, entitlements: entitlements);

        context.HasSucceeded.ShouldBeFalse();

        // No user, so the entitlement is never even queried.
        entitlements.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task It_asks_billing_about_the_requirement_s_own_entitlement()
    {
        var entitlements = new RecordingEntitlementQuery(result: true);
        await DecideAsync(userId: UserId.New(), entitlements: entitlements);

        entitlements.LastEntitlement.ShouldBe(Feature);
    }

    private static async Task<AuthorizationHandlerContext> DecideAsync(
        UserId? userId, bool holdsEntitlement = false, RecordingEntitlementQuery? entitlements = null)
    {
        var handler = new FeatureAuthorizationHandler(
            new StubUserContext(userId),
            entitlements ?? new RecordingEntitlementQuery(holdsEntitlement));

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

    private sealed class RecordingEntitlementQuery(bool result) : IEntitlementQuery
    {
        public int Calls { get; private set; }

        public Entitlement? LastEntitlement { get; private set; }

        public Task<bool> HasEntitlementAsync(
            UserId userId, Entitlement entitlement, CancellationToken cancellationToken)
        {
            Calls++;
            LastEntitlement = entitlement;
            return Task.FromResult(result);
        }
    }
}
