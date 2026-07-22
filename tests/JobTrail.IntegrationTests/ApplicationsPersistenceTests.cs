using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The Applications store's plumbing against real PostgreSQL: an application
/// persists into the module's own schema with a database-generated id and
/// creation timestamp, and its strongly-typed owner id round-trips through the
/// converter. Proves the schema, naming and value-generation conventions hold
/// before the aggregate's behaviour is built on them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApplicationsPersistenceTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_application_persists_with_a_db_generated_id_and_timestamp()
    {
        var ownerId = UserId.New();
        var application = new Application { OwnerId = ownerId };

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            db.Applications.Add(application);
            await db.SaveChangesAsync(Ct);
        }

        // The id and creation time come from the database's uuidv7()/now()
        // defaults, so EF reads them back onto the entity after the insert.
        application.Id.ShouldNotBe(Guid.Empty);
        application.CreatedAt.ShouldNotBe(default);
        application.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task An_application_reads_back_with_its_owner_id_intact()
    {
        var ownerId = UserId.New();
        Guid id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var application = new Application { OwnerId = ownerId };
            db.Applications.Add(application);
            await db.SaveChangesAsync(Ct);
            id = application.Id;
        }

        // A fresh scope, so the read comes from the database, not the tracker:
        // the owner id survives the round trip through the UserId converter.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var reloaded = await db.Applications.SingleOrDefaultAsync(a => a.Id == id, Ct);

            reloaded.ShouldNotBeNull();
            reloaded.OwnerId.ShouldBe(ownerId);
        }
    }

    [Fact]
    public async Task Ownership_queries_filter_by_owner()
    {
        var owner = UserId.New();
        var other = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            db.Applications.Add(new Application { OwnerId = owner });
            db.Applications.Add(new Application { OwnerId = other });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var mine = await db.Applications.Where(a => a.OwnerId == owner).ToListAsync(Ct);

            mine.ShouldHaveSingleItem().OwnerId.ShouldBe(owner);
        }
    }
}
