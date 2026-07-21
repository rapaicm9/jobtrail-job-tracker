using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Account self-service end to end: a user reads and updates their own profile,
/// the token scopes every lookup to the caller's row, and the unauthenticated
/// and malformed cases are turned away.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountEndpointTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Reading_returns_the_callers_own_profile()
    {
        var email = ApiClient.UniqueEmail();
        var tokens = await (await _client.RegisterAsync(email, timeZoneId: "Europe/Belgrade")).ReadTokensAsync();

        var profile = await (await _client.GetAccountAsync(tokens.AccessToken)).ReadProfileAsync();

        profile.UserId.ShouldBe(tokens.UserId);
        profile.Email.ShouldBe(email);
        profile.TimeZoneId.ShouldBe("Europe/Belgrade");
        profile.CreatedAt.ShouldBeGreaterThan(default);
    }

    [Fact]
    public async Task Reading_demands_authentication()
    {
        (await _client.GetAccountAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetAccountAsync("garbage.jwt.value")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Updating_changes_the_timezone_and_returns_the_fresh_profile()
    {
        var tokens = await _client.RegisterNewUserAsync();

        var updated = await (await _client.UpdateAccountAsync(tokens.AccessToken, "America/New_York"))
            .ReadProfileAsync();
        updated.TimeZoneId.ShouldBe("America/New_York");

        // The change persisted: a fresh read sees it too.
        var reread = await (await _client.GetAccountAsync(tokens.AccessToken)).ReadProfileAsync();
        reread.TimeZoneId.ShouldBe("America/New_York");
    }

    [Fact]
    public async Task A_non_iana_timezone_is_a_field_keyed_422()
    {
        var tokens = await _client.RegisterNewUserAsync();

        var response = await _client.UpdateAccountAsync(tokens.AccessToken, "Central Europe Standard Time");

        await response.ShouldBeValidationProblemAsync("timeZoneId");
    }

    [Fact]
    public async Task Updating_demands_authentication()
    {
        (await _client.UpdateAccountAsync(accessToken: null, "Europe/Belgrade"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task One_users_token_never_reads_anothers_account()
    {
        var alice = await (await _client.RegisterAsync(ApiClient.UniqueEmail(), timeZoneId: "Europe/Belgrade"))
            .ReadTokensAsync();
        var bob = await (await _client.RegisterAsync(ApiClient.UniqueEmail(), timeZoneId: "Asia/Tokyo"))
            .ReadTokensAsync();

        // Each token resolves to its own owner's row, never the other's.
        (await (await _client.GetAccountAsync(alice.AccessToken)).ReadProfileAsync())
            .UserId.ShouldBe(alice.UserId);
        (await (await _client.GetAccountAsync(bob.AccessToken)).ReadProfileAsync())
            .TimeZoneId.ShouldBe("Asia/Tokyo");
    }
}
