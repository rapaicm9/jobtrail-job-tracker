using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Analytics.Domain;
using Jobspect.Modules.Analytics.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The Analytics read model against the real database. These rows are the
/// module's system of record - the outbox prunes what it has delivered, so a fact
/// this table cannot hold is one nobody can recover - which makes the shape of
/// the table the thing worth pinning down. Exercised through the context
/// directly: the projections that fill it and the endpoints that read it arrive
/// in later slices.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsPersistenceTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// PostgreSQL keeps timestamps to the microsecond, so a value that made the
    /// round trip is equal to the one sent only once both are cut to the same
    /// precision.
    /// </summary>
    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);

    [Fact]
    public async Task Every_fact_the_events_carry_round_trips()
    {
        var applicationId = Guid.CreateVersion7();
        var ownerId = UserId.New();
        var campaignId = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var appliedDate = new DateOnly(2026, 3, 14);

        var submitted = Truncate(DateTimeOffset.UtcNow.AddDays(-30));
        var screening = Truncate(DateTimeOffset.UtcNow.AddDays(-25));
        var interview = Truncate(DateTimeOffset.UtcNow.AddDays(-20));
        var offer = Truncate(DateTimeOffset.UtcNow.AddDays(-10));
        var closed = Truncate(DateTimeOffset.UtcNow.AddDays(-5));

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            db.ApplicationFacts.Add(new ApplicationFacts
            {
                ApplicationId = applicationId,
                OwnerId = ownerId,
                CampaignId = campaignId,
                CompanyId = companyId,
                AppliedDate = appliedDate,
                Source = "LinkedIn",
                WorkMode = "Hybrid",
                Stage = "Accepted",
                Outcome = "Accepted",
                StageEnteredAt = closed,
                ClosedAt = closed,
                StageRecordedAt = closed,
                CampaignRecordedAt = submitted,
                FirstResponseAt = screening,
                ReachedScreeningAt = screening,
                ReachedInterviewAt = interview,
                ReachedOfferAt = offer,
                FirstInterviewScheduledAt = interview,
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            var facts = await db.ApplicationFacts.SingleAsync(f => f.ApplicationId == applicationId, Ct);

            facts.OwnerId.ShouldBe(ownerId);
            facts.CampaignId.ShouldBe(campaignId);
            facts.CompanyId.ShouldBe(companyId);
            facts.AppliedDate.ShouldBe(appliedDate);
            facts.Source.ShouldBe("LinkedIn");
            facts.WorkMode.ShouldBe("Hybrid");
            facts.Stage.ShouldBe("Accepted");
            facts.Outcome.ShouldBe("Accepted");
            facts.StageEnteredAt.ShouldBe(closed);
            facts.ClosedAt.ShouldBe(closed);

            // The watermarks the projections guard their writes with.
            facts.StageRecordedAt.ShouldBe(closed);
            facts.CampaignRecordedAt.ShouldBe(submitted);

            // The monotone facts every rate, the funnel and time-in-stage read.
            facts.FirstResponseAt.ShouldBe(screening);
            facts.ReachedScreeningAt.ShouldBe(screening);
            facts.ReachedInterviewAt.ShouldBe(interview);
            facts.ReachedOfferAt.ShouldBe(offer);
            facts.FirstInterviewScheduledAt.ShouldBe(interview);

            facts.CreatedAt.ShouldNotBe(default);   // now() assigned by the DB
        }
    }

    [Fact]
    public async Task The_key_is_the_application_id_the_event_carried()
    {
        // Every other table in this system mints its key with a uuidv7() default.
        // This one must not: the id arrives on the event, and a projection that
        // upserts on a key the database had rewritten would insert a second row
        // for the same application on every redelivery.
        var applicationId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            db.ApplicationFacts.Add(new ApplicationFacts
            {
                ApplicationId = applicationId,
                OwnerId = UserId.New(),
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            (await db.ApplicationFacts.AnyAsync(f => f.ApplicationId == applicationId, Ct)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task A_row_exists_before_its_campaign_and_applied_date_are_known()
    {
        // The case that forces those two columns to be nullable, against the
        // instinct that every application obviously has both. Only
        // ApplicationSubmitted carries them, and delivery is unordered - so an
        // application whose stage change arrives first has to be recordable
        // without them. Refusing the row would drop the stage change instead,
        // which is the loss the read model exists to prevent.
        var applicationId = Guid.CreateVersion7();
        var moved = Truncate(DateTimeOffset.UtcNow);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            db.ApplicationFacts.Add(new ApplicationFacts
            {
                ApplicationId = applicationId,
                OwnerId = UserId.New(),
                Stage = "Screening",
                StageEnteredAt = moved,
                StageRecordedAt = moved,
                FirstResponseAt = moved,
                ReachedScreeningAt = moved,
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            var facts = await db.ApplicationFacts.SingleAsync(f => f.ApplicationId == applicationId, Ct);

            facts.CampaignId.ShouldBeNull();
            facts.AppliedDate.ShouldBeNull();
            facts.Stage.ShouldBe("Screening");

            // Nothing has closed it, and nothing it never reached is claimed.
            facts.Outcome.ShouldBeNull();
            facts.ClosedAt.ShouldBeNull();
            facts.ReachedInterviewAt.ShouldBeNull();
            facts.ReachedOfferAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Rows_are_read_back_by_owner()
    {
        // The access path every figure and the erasure both take, and the reason
        // the one index leads on the owner.
        var mine = UserId.New();
        var theirs = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
            db.ApplicationFacts.AddRange(
                new ApplicationFacts { ApplicationId = Guid.CreateVersion7(), OwnerId = mine, Stage = "Applied" },
                new ApplicationFacts { ApplicationId = Guid.CreateVersion7(), OwnerId = mine, Stage = "Offer" },
                new ApplicationFacts { ApplicationId = Guid.CreateVersion7(), OwnerId = theirs, Stage = "Applied" });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

            (await db.ApplicationFacts.CountAsync(f => f.OwnerId == mine, Ct)).ShouldBe(2);
            (await db.ApplicationFacts.CountAsync(f => f.OwnerId == theirs, Ct)).ShouldBe(1);
        }
    }
}
