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
/// creation timestamp, its strongly-typed owner id round-trips through the
/// converter, and every built-in field - including the optional compensation
/// complex type - survives a round trip. Proves the schema, naming and
/// value-generation conventions hold before the aggregate's behaviour is built on
/// them.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApplicationsPersistenceTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_application_persists_with_a_db_generated_id_and_timestamp()
    {
        var ownerId = UserId.New();
        var campaignId = await SeedCampaignAsync(ownerId);
        var application = NewApplication(ownerId, campaignId);

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
        var campaignId = await SeedCampaignAsync(ownerId);
        Guid id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var application = NewApplication(ownerId, campaignId);
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
    public async Task An_application_round_trips_its_built_in_fields()
    {
        var ownerId = UserId.New();
        var campaignId = await SeedCampaignAsync(ownerId);
        Guid id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var application = new Application
            {
                OwnerId = ownerId,
                CampaignId = campaignId,
                Role = "Staff Backend Engineer",
                Compensation = new Money(120_000.50m, "EUR"),
                Location = "Belgrade",
                WorkMode = WorkMode.Remote,
                PostingUrl = "https://example.com/jobs/42",
                Source = "LinkedIn",
                AppliedDate = new DateOnly(2026, 7, 24),
                ApplicationDeadline = new DateOnly(2026, 8, 1),
                OfferDecisionDeadline = new DateOnly(2026, 8, 15),
                CvLabel = "cv-backend-v3",
                CoverLetterLabel = "cover-acme",
            };
            db.Applications.Add(application);
            await db.SaveChangesAsync(Ct);
            id = application.Id;
        }

        // A fresh scope forces the read from PostgreSQL: the scalar columns, the
        // date columns, the string-stored enum, and the two-column optional
        // compensation complex type all come back as they went in.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var reloaded = await db.Applications.SingleOrDefaultAsync(a => a.Id == id, Ct);

            reloaded.ShouldNotBeNull();
            reloaded.CampaignId.ShouldBe(campaignId);
            reloaded.Role.ShouldBe("Staff Backend Engineer");
            reloaded.Compensation.ShouldBe(new Money(120_000.50m, "EUR"));
            reloaded.Location.ShouldBe("Belgrade");
            reloaded.WorkMode.ShouldBe(WorkMode.Remote);
            reloaded.PostingUrl.ShouldBe("https://example.com/jobs/42");
            reloaded.Source.ShouldBe("LinkedIn");
            reloaded.AppliedDate.ShouldBe(new DateOnly(2026, 7, 24));
            reloaded.ApplicationDeadline.ShouldBe(new DateOnly(2026, 8, 1));
            reloaded.OfferDecisionDeadline.ShouldBe(new DateOnly(2026, 8, 15));
            reloaded.CvLabel.ShouldBe("cv-backend-v3");
            reloaded.CoverLetterLabel.ShouldBe("cover-acme");
        }
    }

    [Fact]
    public async Task An_application_with_no_compensation_reads_back_null()
    {
        var ownerId = UserId.New();
        var campaignId = await SeedCampaignAsync(ownerId);
        Guid id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var application = NewApplication(ownerId, campaignId);
            db.Applications.Add(application);
            await db.SaveChangesAsync(Ct);
            id = application.Id;
        }

        // Both compensation columns are null, so the optional complex type
        // materializes back as a null Money rather than a zero-amount value.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var reloaded = await db.Applications.SingleOrDefaultAsync(a => a.Id == id, Ct);

            reloaded.ShouldNotBeNull();
            reloaded.Compensation.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Ownership_queries_filter_by_owner()
    {
        var owner = UserId.New();
        var other = UserId.New();
        var ownerCampaign = await SeedCampaignAsync(owner);
        var otherCampaign = await SeedCampaignAsync(other);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            db.Applications.Add(NewApplication(owner, ownerCampaign));
            db.Applications.Add(NewApplication(other, otherCampaign));
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            var mine = await db.Applications.Where(a => a.OwnerId == owner).ToListAsync(Ct);

            mine.ShouldHaveSingleItem().OwnerId.ShouldBe(owner);
        }
    }

    /// <summary>
    /// Inserts a default campaign for <paramref name="owner"/> and returns its id -
    /// an application's <c>CampaignId</c> is a required foreign key, so every test
    /// application needs a real campaign to sit in.
    /// </summary>
    private async Task<Guid> SeedCampaignAsync(UserId owner)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        var campaign = new Campaign { OwnerId = owner, Name = Campaign.DefaultName, IsDefault = true };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.Id;
    }

    /// <summary>A minimally-valid application: its owner, its required campaign, and the one required built-in field.</summary>
    private static Application NewApplication(UserId owner, Guid campaignId) =>
        new() { OwnerId = owner, CampaignId = campaignId, Role = "Backend Engineer" };
}
