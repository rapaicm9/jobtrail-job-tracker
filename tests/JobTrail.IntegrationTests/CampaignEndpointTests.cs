using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The campaign surface against a real database: a Pro account opens further
/// campaigns beside the one it was given, renames them, reads them whole, and
/// deletes them without losing what they held.
/// <para>
/// Every test here works from a Pro account, because opening a second campaign is
/// the paid capability. <see cref="CampaignGateTests"/> is where the gate itself -
/// and what an account that has lost it may still do - is the subject.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CampaignEndpointTests(ApiFixture fixture)
{
    /// <summary>
    /// The module's cap, spelled out here rather than read from it: a test that
    /// takes the limit from the code it is checking proves only that the code
    /// agrees with itself, and this number is part of the contract.
    /// </summary>
    private const int CampaignLimit = 20;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Opening_a_campaign_returns_it_with_a_location()
    {
        var tokens = await ProUserAsync();

        var response = await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "  2026 backend roles  " });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadCampaignAsync();

        // Trimmed, and not the default - that one came with the account.
        created.Name.ShouldBe("2026 backend roles");
        created.IsDefault.ShouldBeFalse();
        created.ApplicationCount.ShouldBe(0);
        created.UpdatedAt.ShouldBeNull();

        response.Headers.Location?.ToString().ShouldBe($"/api/v1/campaigns/{created.Id}");
    }

    [Fact]
    public async Task An_account_starts_with_one_default_campaign()
    {
        var tokens = await ProUserAsync();

        var listed = await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync();

        var only = listed.ShouldHaveSingleItem();
        only.IsDefault.ShouldBeTrue();
        only.Name.ShouldBe("My Applications");
    }

    [Fact]
    public async Task Campaigns_list_oldest_first_which_puts_the_default_on_top()
    {
        var tokens = await ProUserAsync();
        await CreateAsync(tokens.AccessToken, "Second");
        await CreateAsync(tokens.AccessToken, "Third");

        var listed = await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync();

        // The default was created with the account, so ordering by age alone puts
        // it first - no special case in the query.
        listed.Select(c => c.Name).ShouldBe(["My Applications", "Second", "Third"]);
        listed[0].IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task A_campaign_reads_back_by_id()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Contract work");

        var fetched = await (await _client.GetCampaignAsync(tokens.AccessToken, created.Id)).ReadCampaignAsync();

        fetched.ShouldBe(created);
    }

    [Fact]
    public async Task A_name_already_in_use_is_refused_however_it_is_cased()
    {
        var tokens = await ProUserAsync();
        await CreateAsync(tokens.AccessToken, "Backend roles");

        // Two campaigns differing only in case are one name to anyone reading a
        // picker, so the database compares them folded.
        await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "backend ROLES" }))
            .ShouldBeProblemAsync(409, "campaign.name_taken");
    }

    [Fact]
    public async Task Another_accounts_name_is_not_in_the_way()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();

        await CreateAsync(theirs.AccessToken, "Backend roles");

        // Uniqueness is per account: what someone else calls their search has
        // nothing to do with mine.
        (await _client.CreateCampaignAsync(mine.AccessToken, new { name = "Backend roles" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_campaign_needs_a_name(string? name)
    {
        var tokens = await ProUserAsync();

        await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name }))
            .ShouldBeValidationProblemAsync("name");
    }

    [Fact]
    public async Task A_name_longer_than_the_cap_is_refused()
    {
        var tokens = await ProUserAsync();

        await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = new string('n', 101) }))
            .ShouldBeValidationProblemAsync("name");
    }

    [Fact]
    public async Task An_account_may_not_exceed_its_campaign_budget()
    {
        var tokens = await ProUserAsync();

        // The default counts as one of them, so the account can add the rest.
        for (var index = 0; index < CampaignLimit - 1; index++)
        {
            await CreateAsync(tokens.AccessToken, $"Campaign {index}");
        }

        await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "One too many" }))
            .ShouldBeProblemAsync(409, "campaign.limit_reached");
    }

    [Fact]
    public async Task A_campaign_is_renamed()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Backend roles");

        var renamed = await (await _client.UpdateCampaignAsync(
            tokens.AccessToken, created.Id, new { name = "Platform roles" })).ReadCampaignAsync();

        renamed.Name.ShouldBe("Platform roles");
        renamed.UpdatedAt.ShouldNotBeNull();

        // And the old name is free again.
        (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "Backend roles" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_default_campaign_is_renamed_like_any_other()
    {
        var tokens = await ProUserAsync();
        var defaultId = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);

        // The default is the account's own search, and "My Applications" is only
        // where its name starts. It stays the default through the rename.
        var renamed = await (await _client.UpdateCampaignAsync(
            tokens.AccessToken, defaultId, new { name = "Everything" })).ReadCampaignAsync();

        renamed.Name.ShouldBe("Everything");
        renamed.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Renaming_onto_a_name_already_in_use_is_refused()
    {
        var tokens = await ProUserAsync();
        await CreateAsync(tokens.AccessToken, "Backend roles");
        var other = await CreateAsync(tokens.AccessToken, "Contract work");

        await (await _client.UpdateCampaignAsync(tokens.AccessToken, other.Id, new { name = "Backend roles" }))
            .ShouldBeProblemAsync(409, "campaign.name_taken");
    }

    [Fact]
    public async Task A_campaign_is_deleted_and_stops_being_listed()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Abandoned search");

        (await _client.DeleteCampaignAsync(tokens.AccessToken, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await _client.GetCampaignAsync(tokens.AccessToken, created.Id))
            .ShouldBeProblemAsync(404, "campaign.not_found");

        var listed = await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync();
        listed.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_a_campaign_moves_its_applications_to_the_default()
    {
        var tokens = await ProUserAsync();
        var ownerId = UserId.From(tokens.UserId);
        var defaultId = await fixture.DefaultCampaignIdAsync(ownerId, Ct);
        var created = await CreateAsync(tokens.AccessToken, "Abandoned search");

        var applicationId = await SeedApplicationAsync(ownerId, created.Id);

        (await _client.DeleteCampaignAsync(tokens.AccessToken, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The application outlives the campaign it sat in - that is the whole
        // reason a campaign may be deleted while a custom field may not.
        var application = await (await _client.GetApplicationAsync(tokens.AccessToken, applicationId))
            .ReadApplicationAsync();

        application.CampaignId.ShouldBe(defaultId);

        // And it is stamped as changed, because it was: a client holding a copy has
        // no other way to notice the move.
        application.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_default_campaign_cannot_be_deleted()
    {
        var tokens = await ProUserAsync();
        var defaultId = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);

        // It is where a deleted campaign's applications are sent, so it is the one
        // campaign with nowhere to send its own.
        await (await _client.DeleteCampaignAsync(tokens.AccessToken, defaultId))
            .ShouldBeProblemAsync(409, "campaign.default_not_deletable");
    }

    [Fact]
    public async Task Deleting_the_same_campaign_twice_is_a_404()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Abandoned search");

        (await _client.DeleteCampaignAsync(tokens.AccessToken, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await _client.DeleteCampaignAsync(tokens.AccessToken, created.Id))
            .ShouldBeProblemAsync(404, "campaign.not_found");
    }

    [Fact]
    public async Task The_list_counts_what_each_campaign_holds()
    {
        var tokens = await ProUserAsync();
        var ownerId = UserId.From(tokens.UserId);
        var created = await CreateAsync(tokens.AccessToken, "Backend roles");

        await SeedApplicationAsync(ownerId, created.Id);
        await SeedApplicationAsync(ownerId, created.Id);

        var listed = await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync();

        listed.Single(c => c.IsDefault).ApplicationCount.ShouldBe(0);
        listed.Single(c => c.Id == created.Id).ApplicationCount.ShouldBe(2);
    }

    [Fact]
    public async Task Another_users_campaign_is_a_404_whatever_is_asked_of_it()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var created = await CreateAsync(theirs.AccessToken, "Not yours");

        // A 404 rather than a 403 on every verb: the difference would confirm the
        // campaign exists.
        await (await _client.GetCampaignAsync(mine.AccessToken, created.Id))
            .ShouldBeProblemAsync(404, "campaign.not_found");

        await (await _client.UpdateCampaignAsync(mine.AccessToken, created.Id, new { name = "Mine now" }))
            .ShouldBeProblemAsync(404, "campaign.not_found");

        await (await _client.DeleteCampaignAsync(mine.AccessToken, created.Id))
            .ShouldBeProblemAsync(404, "campaign.not_found");

        // And it is untouched.
        (await _client.GetCampaignAsync(theirs.AccessToken, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_campaign_is_a_404()
    {
        var tokens = await ProUserAsync();

        await (await _client.GetCampaignAsync(tokens.AccessToken, Guid.CreateVersion7()))
            .ShouldBeProblemAsync(404, "campaign.not_found");
    }

    [Fact]
    public async Task Every_campaign_route_demands_authentication()
    {
        var id = Guid.CreateVersion7();

        (await _client.ListCampaignsAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetCampaignAsync(accessToken: null, id)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.UpdateCampaignAsync(accessToken: null, id, new { name = "Anything" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.DeleteCampaignAsync(accessToken: null, id)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A fresh Pro account with its default campaign - opening a second one is the paid capability.</summary>
    private Task<AuthTokens> ProUserAsync() => fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

    private async Task<CampaignView> CreateAsync(string? accessToken, string name) =>
        await (await _client.CreateCampaignAsync(accessToken, new { name })).ReadCampaignAsync();

    /// <summary>
    /// Puts an application in a named campaign through the store, because nothing
    /// in the API places one anywhere but the default yet - that lands with the
    /// move slices.
    /// </summary>
    private async Task<Guid> SeedApplicationAsync(UserId ownerId, Guid campaignId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var application = new Application
        {
            OwnerId = ownerId,
            CampaignId = campaignId,
            Role = "Backend Engineer",
        };

        db.Applications.Add(application);
        await db.SaveChangesAsync(Ct);

        return application.Id;
    }
}
