using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Reads and writes the Billing store directly, for tests that set up or inspect
/// a plan without an endpoint to do it through. Each call takes its own scope, so
/// a read never sees a write's still-tracked entity.
/// </summary>
internal static class BillingScenario
{
    public static async Task SeedPlanAsync(
        this ApiFixture fixture, UserId userId, PlanTier tier, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        db.Plans.Add(new Plan { UserId = userId, Tier = tier });
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
