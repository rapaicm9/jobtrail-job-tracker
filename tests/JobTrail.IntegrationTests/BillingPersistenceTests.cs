using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The Billing store against the real database: a plan round-trips through the
/// billing schema with its DB-side defaults, and the one-plan-per-user rule is
/// the database's, not the code's. Exercised through the context directly - the
/// module has no endpoints yet.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingPersistenceTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_plan_round_trips_with_its_database_defaults()
    {
        var userId = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.Plans.Add(new Plan { UserId = userId, Tier = PlanTier.Free });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var plan = await db.Plans.SingleAsync(p => p.UserId == userId, Ct);

            plan.Tier.ShouldBe(PlanTier.Free);
            plan.Id.ShouldNotBe(Guid.Empty);       // uuidv7() assigned by the DB
            plan.CreatedAt.ShouldNotBe(default);    // now() assigned by the DB
            plan.UpdatedAt.ShouldBeNull();          // untouched until the tier changes
        }
    }

    [Fact]
    public async Task A_user_can_hold_only_one_plan()
    {
        var userId = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.Plans.Add(new Plan { UserId = userId, Tier = PlanTier.Free });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.Plans.Add(new Plan { UserId = userId, Tier = PlanTier.Pro });

            // The unique index on user_id is the arbiter, not a pre-check.
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(Ct));
        }
    }
}
