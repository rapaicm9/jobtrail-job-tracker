using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features.ProvisionCampaign;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Default-campaign provisioning off <c>UserRegistered</c>: registration stands
/// up a campaign through the event bus, redelivery leaves exactly one, and the
/// partial unique index lets a user hold other campaigns while pinning the
/// default to one - the index, not a pre-check, is what makes the handler
/// idempotent and the invariant true.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CampaignProvisioningTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Registering_provisions_a_default_campaign()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // The campaign is created asynchronously as the event is dispatched.
        await Poll.UntilAsync(
            async () => await DefaultCampaignNameFor(userId) == Campaign.DefaultName,
            "registration should provision a default campaign",
            Ct);
    }

    [Fact]
    public async Task Provisioning_the_same_user_twice_leaves_one_campaign()
    {
        var userId = UserId.New();

        // Two deliveries, each in its own scope - as the dispatcher would run them.
        await ProvisionAsync(userId);
        await ProvisionAsync(userId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        (await db.Campaigns.CountAsync(c => c.OwnerId == userId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_user_may_hold_other_campaigns_but_only_one_default()
    {
        var owner = UserId.New();
        await ProvisionAsync(owner);

        // A second, non-default campaign for the same owner is allowed - the Pro
        // future where a user runs more than one search.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            db.Campaigns.Add(new Campaign { OwnerId = owner, Name = "2026 backend roles", IsDefault = false });
            await db.SaveChangesAsync(Ct);
        }

        // A second default for the same owner is not - the partial unique index rejects it.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            db.Campaigns.Add(new Campaign { OwnerId = owner, Name = "Another default", IsDefault = true });
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(Ct));
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
            (await db.Campaigns.CountAsync(c => c.OwnerId == owner, Ct)).ShouldBe(2);
            (await db.Campaigns.CountAsync(c => c.OwnerId == owner && c.IsDefault, Ct)).ShouldBe(1);
        }
    }

    /// <summary>Runs the handler once, the way a single event delivery would.</summary>
    private async Task ProvisionAsync(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider
            .GetServices<IEventHandler<UserRegistered>>()
            .OfType<CampaignProvisioningHandler>()
            .Single();

        await handler.HandleAsync(new UserRegistered(userId), Ct);
    }

    private async Task<string?> DefaultCampaignNameFor(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        var campaign = await db.Campaigns
            .SingleOrDefaultAsync(c => c.OwnerId == userId && c.IsDefault, Ct);
        return campaign?.Name;
    }
}
