using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Reads and writes the Billing store directly, for tests that set up or inspect
/// a plan without an endpoint to do it through. Each call takes its own scope, so
/// a read never sees a write's still-tracked entity.
/// </summary>
internal static class BillingScenario
{
    /// <summary>
    /// A fresh account with Pro unlocked, for the tests whose subject is a gated
    /// capability rather than the unlock itself. Goes through the real purchase
    /// endpoint rather than the developer grant, which only exists in Development -
    /// so this works against the shared host whatever environment it runs as.
    /// </summary>
    public static async Task<AuthTokens> RegisterProUserAsync(
        this ApiFixture fixture, HttpClient client, CancellationToken cancellationToken)
    {
        var tokens = await client.RegisterNewUserAsync();
        await fixture.UnlockProAsync(client, tokens, cancellationToken);

        return tokens;
    }

    /// <summary>Buys Pro for an account a test already holds tokens for.</summary>
    public static async Task UnlockProAsync(
        this ApiFixture fixture, HttpClient client, AuthTokens tokens, CancellationToken cancellationToken)
    {
        // The Free plan is provisioned asynchronously off UserRegistered; there is
        // nothing for the purchase to upgrade until it lands.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(UserId.From(tokens.UserId), cancellationToken) is not null,
            "registration should provision the plan the purchase upgrades",
            cancellationToken);

        (await client.PurchaseProAsync(tokens.AccessToken)).IsSuccessStatusCode.ShouldBeTrue(
            "the mock purchase should unlock Pro");
    }

    public static async Task SeedPlanAsync(
        this ApiFixture fixture, UserId userId, PlanTier tier, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        db.Plans.Add(new Plan { UserId = userId, Tier = tier });
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedPurchaseAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        db.Purchases.Add(new Purchase { UserId = userId, ProviderReference = "seed_ref" });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Drops an account back to Free. There is no downgrade in the product - Pro is
    /// bought once - but the entitlement is modelled as something that can be
    /// absent, and the retained-read behaviour only means anything if a test can
    /// put an account in that state.
    /// </summary>
    public static async Task SetTierToFreeAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        var plan = await db.Plans.SingleAsync(p => p.UserId == userId, cancellationToken);
        plan.Tier = PlanTier.Free;
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task<Plan?> PlanForAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await db.Plans.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    public static async Task<int> PurchaseCountAsync(
        this ApiFixture fixture, UserId userId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await db.Purchases.CountAsync(p => p.UserId == userId, cancellationToken);
    }
}
