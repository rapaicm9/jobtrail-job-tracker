using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/applications</c> against a real database: a user's own
/// applications, newest applied first, and never anyone else's.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListApplicationsEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Returns_the_callers_applications_newest_applied_first()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        await CreateAsync(tokens.AccessToken, "Middle", "2026-07-15");
        await CreateAsync(tokens.AccessToken, "Newest", "2026-07-20");
        await CreateAsync(tokens.AccessToken, "Oldest", "2026-07-10");

        var applications = await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync();

        applications.Select(a => a.Role).ShouldBe(["Newest", "Middle", "Oldest"]);
    }

    [Fact]
    public async Task Does_not_return_another_users_applications()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        await CreateAsync(theirs.AccessToken, "Theirs", "2026-07-20");
        await CreateAsync(mine.AccessToken, "Mine", "2026-07-20");

        var applications = await (await _client.ListApplicationsAsync(mine.AccessToken)).ReadApplicationListAsync();

        applications.ShouldHaveSingleItem().Role.ShouldBe("Mine");
    }

    [Fact]
    public async Task Returns_empty_when_the_user_has_none()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var applications = await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync();

        applications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.ListApplicationsAsync(accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task CreateAsync(string? accessToken, string role, string appliedDate) =>
        (await _client.CreateApplicationAsync(accessToken, new { role, appliedDate }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
}
