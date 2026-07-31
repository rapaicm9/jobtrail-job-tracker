using JobTrail.Infrastructure.Persistence;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The deploy-time migration run. Every module registers a migrator beside its own
/// store, and one process applies all of them and exits before either host starts -
/// so this asserts what that process finds, and what it leaves behind.
/// <para>
/// The whole suite already leans on it, because the fixture migrates through the
/// same fan-out. What that cannot catch is a module registering no migrator at
/// all: its schema would simply never be created, and every one of its tests would
/// fail a long way from the cause.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleMigrationTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One schema per module - the rule the whole design rests on.</summary>
    private static readonly string[] Schemas =
        ["identity", "billing", "applications", "analytics", "notifications"];

    [Fact]
    public void Every_module_offers_its_store_to_the_migration_run()
    {
        using var scope = fixture.CreateScope();
        var migrators = scope.ServiceProvider.GetServices<IModuleMigrator>().ToArray();

        // One per module, each naming a different store. A module composed into a
        // host without one is skipped by the deploy in silence.
        migrators.Length.ShouldBe(Schemas.Length);
        migrators.Select(m => m.Store).ShouldBeUnique();
    }

    [Fact]
    public async Task Running_it_leaves_every_module_schema_in_place()
    {
        // The fixture ran the migrators before the first test; this asserts the
        // outcome rather than running them again.
        var present = await QueryAsync(
            """
            SELECT schema_name AS "Value" FROM information_schema.schemata
            WHERE schema_name = ANY({0})
            """);

        present.ShouldBe(Schemas, ignoreOrder: true);
    }

    [Fact]
    public async Task Each_module_keeps_its_own_migration_history()
    {
        // Separate history tables are what let modules migrate independently: one
        // module's pending migration is not another's problem, and the run applies
        // each store's own outstanding set.
        var histories = await QueryAsync(
            """
            SELECT table_schema AS "Value" FROM information_schema.tables
            WHERE table_name = '__ef_migrations_history' AND table_schema = ANY({0})
            """);

        histories.ShouldBe(Schemas, ignoreOrder: true);
    }

    /// <summary>
    /// Reads the catalogue through one module's context. Any of them would do -
    /// they share a database and differ only in the schema they own - and the
    /// catalogue belongs to none of them.
    /// </summary>
    private async Task<List<string>> QueryAsync(string sql)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await db.Database.SqlQueryRaw<string>(sql, [Schemas]).ToListAsync(Ct);
    }
}
