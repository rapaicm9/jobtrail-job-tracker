using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Features.ProvisionPlan;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

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

        // Two deliveries, each in its own scope - as the dispatcher would run them.
        await ProvisionAsync(userId);
        await ProvisionAsync(userId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        (await db.Plans.CountAsync(p => p.UserId == userId, Ct)).ShouldBe(1);
    }

    /// <summary>Runs the handler once, the way a single event delivery would.</summary>
    private async Task ProvisionAsync(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetServices<IEventHandler<UserRegistered>>()
            .OfType<PlanProvisioningHandler>()
            .Single();

        await handler.HandleAsync(new UserRegistered(userId), Ct);
    }

    private async Task<PlanTier?> TierFor(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.UserId == userId, Ct);
        return plan?.Tier;
    }
}
