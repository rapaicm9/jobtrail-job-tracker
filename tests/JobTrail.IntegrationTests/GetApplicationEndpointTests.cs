using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/applications/{id}</c> against a real database: ownership is the
/// query, so another user's application - or one that never existed - is a 404,
/// never a 403 that would confirm it exists. The happy read is covered by the
/// create round-trip; this pins the boundaries.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GetApplicationEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Returns_404_for_another_users_application()
    {
        var owner = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var other = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var mine = await (await _client.CreateApplicationAsync(
            owner.AccessToken, new { role = "Backend Engineer" })).ReadApplicationAsync();

        var response = await _client.GetApplicationAsync(other.AccessToken, mine.Id);

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Returns_404_for_an_application_that_does_not_exist()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.GetApplicationAsync(tokens.AccessToken, Guid.NewGuid());

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.GetApplicationAsync(accessToken: null, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
