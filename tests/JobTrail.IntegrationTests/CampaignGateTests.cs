using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// What <c>Feature:MultipleCampaigns</c> actually gates, proved at the edge over
/// the real store.
/// <para>
/// The entitlement is the right to <em>hold</em> more than one campaign, so it sits
/// on the one endpoint that creates a second and nowhere else. Everything else here
/// works on campaigns the account already has, and an account that has lost the
/// entitlement must keep all of it: reading, so a client can still name the campaign
/// an application sits in; renaming, which is cosmetic; and above all deleting,
/// because reassign-to-default is the only road back to a single campaign. Gate that
/// and the account is stuck in a shape it can no longer reduce.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CampaignGateTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Pro_may_open_another_campaign()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "2026 backend roles" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Free_is_forbidden_from_opening_a_second_campaign()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // Waiting for the plan means the 403 is the entitlement being absent rather
        // than the plan not existing yet - otherwise this would pass for the wrong
        // reason.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(UserId.From(tokens.UserId), Ct) is not null,
            "registration should provision the Free plan the gate reads",
            Ct);

        (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "A second search" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Free_still_has_exactly_one_campaign_and_can_see_it()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var listed = await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync();

        var only = listed.ShouldHaveSingleItem();
        only.IsDefault.ShouldBeTrue();

        (await _client.GetCampaignAsync(tokens.AccessToken, only.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_anonymous_caller_is_challenged_rather_than_forbidden()
    {
        // 401, not 403: the question is "which user" before "may that user", so a
        // caller who has not said who they are is asked, not refused.
        (await _client.CreateCampaignAsync(accessToken: null, new { name = "Anything" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_downgraded_account_keeps_the_campaigns_it_has_and_can_still_be_rid_of_them()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);
        var created = await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "Backend roles" }))
            .ReadCampaignAsync();

        // The same account without the entitlement is what a downgrade looks like.
        await fixture.SetTierToFreeAsync(ownerId, Ct);

        // It still holds both, and can still read them.
        var listed = await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync();
        listed.Count.ShouldBe(2);

        // Renaming is cosmetic and stays open.
        (await _client.UpdateCampaignAsync(tokens.AccessToken, created.Id, new { name = "Old search" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // And deleting stays open, because it is the only way back to one campaign.
        (await _client.DeleteCampaignAsync(tokens.AccessToken, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await (await _client.ListCampaignsAsync(tokens.AccessToken)).ReadCampaignListAsync())
            .ShouldHaveSingleItem().IsDefault.ShouldBeTrue();

        // What it may not do is open another.
        (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "A fresh start" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
