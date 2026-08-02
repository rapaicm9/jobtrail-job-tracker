using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Billing.Domain;
using Jobspect.Modules.Billing.Features.ProvisionPlan;
using Jobspect.Modules.Billing.Persistence;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Free-plan provisioning off <c>UserRegistered</c>: registration stands up a
/// plan through the event bus, and redelivery leaves exactly one - the unique
/// index, not a pre-check, is what makes the handler idempotent.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingProvisioningTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Registering_provisions_a_free_plan()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // The plan is created asynchronously as the event is dispatched.
        await Poll.UntilAsync(
            async () => await TierFor(userId) == PlanTier.Free,
            "registration should provision a Free plan",
            Ct);
    }

    [Fact]
    public async Task Provisioning_the_same_user_twice_leaves_one_plan()
    {
        var userId = UserId.New();

        // Two deliveries, each in its own scope - the redelivery an at-least-once
        // dispatcher makes when a row is claimed again after a failure.
        await ProvisionAsync(userId);
        await ProvisionAsync(userId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await db.Plans.CountAsync(p => p.UserId == userId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_collision_does_not_swallow_the_next_account_in_the_batch()
    {
        var taken = UserId.New();
        var fresh = UserId.New();

        await ProvisionAsync(taken);

        // One scope, two deliveries - the shape the outbox dispatcher runs a batch
        // in, and the reason the swallowed collision has to detach what it added.
        // Left tracked, the first delivery's failed insert would be re-attempted by
        // the second delivery's save, fail on the same index, and be swallowed
        // again - leaving a brand new account with no plan and no entitlements.
        using var scope = fixture.CreateScope();
        var handler = HandlerIn(scope);

        await handler.HandleAsync(new UserRegistered(Guid.CreateVersion7(), taken), Ct);
        await handler.HandleAsync(new UserRegistered(Guid.CreateVersion7(), fresh), Ct);

        (await TierFor(fresh)).ShouldBe(PlanTier.Free);
    }

    /// <summary>Runs the handler once in a scope of its own, as one delivery would.</summary>
    private async Task ProvisionAsync(UserId userId)
    {
        using var scope = fixture.CreateScope();

        await HandlerIn(scope).HandleAsync(new UserRegistered(Guid.CreateVersion7(), userId), Ct);
    }

    /// <summary>
    /// The registered handler, resolved the way the dispatcher resolves it - so a
    /// handler that stopped being registered fails here rather than passing on a
    /// hand-built instance.
    /// </summary>
    private static PlanProvisioningHandler HandlerIn(IServiceScope scope) =>
        scope.ServiceProvider
            .GetServices<IEventHandler<UserRegistered>>()
            .OfType<PlanProvisioningHandler>()
            .Single();

    private async Task<PlanTier?> TierFor(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.UserId == userId, Ct);
        return plan?.Tier;
    }
}
