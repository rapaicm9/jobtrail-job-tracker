using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The rotation, reuse-detection and revocation story end to end - the flows
/// ADR-0003 calls out as the correctness burden of self-issued tokens.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TokenLifecycleTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Refreshing_rotates_the_pair()
    {
        var issued = await _client.RegisterNewUserAsync();

        var response = await _client.RefreshAsync(issued.RefreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = await response.ReadTokensAsync();
        rotated.UserId.ShouldBe(issued.UserId);
        rotated.RefreshToken.ShouldNotBe(issued.RefreshToken);
        rotated.AccessToken.ShouldNotBe(issued.AccessToken);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_family()
    {
        var issued = await _client.RegisterNewUserAsync();
        var rotated = await (await _client.RefreshAsync(issued.RefreshToken)).ReadTokensAsync();

        // Replay of the retired token: rejected, and treated as compromise.
        var replay = await _client.RefreshAsync(issued.RefreshToken);
        await replay.ShouldBeProblemAsync(401, "refresh_token.reuse_detected");

        // The legitimate successor is collateral - the family is dead.
        var successor = await _client.RefreshAsync(rotated.RefreshToken);
        await successor.ShouldBeProblemAsync(401, "refresh_token.reuse_detected");
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        var response = await _client.RefreshAsync("not-a-token-anyone-issued");

        await response.ShouldBeProblemAsync(401, "refresh_token.invalid");
    }

    [Fact]
    public async Task Per_device_logout_kills_that_session_only()
    {
        var email = ApiClient.UniqueEmail();
        var device1 = await (await _client.RegisterAsync(email)).ReadTokensAsync();
        var device2 = await (await _client.LoginAsync(email)).ReadTokensAsync();

        (await _client.LogoutAsync(device1.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The logged-out device's token is gone; the other device still refreshes.
        await (await _client.RefreshAsync(device1.RefreshToken))
            .ShouldBeProblemAsync(401, "refresh_token.invalid");
        (await _client.RefreshAsync(device2.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_is_idempotent_about_unknown_tokens()
    {
        var response = await _client.LogoutAsync("never-issued");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Global_logout_kills_access_and_refresh_tokens_everywhere()
    {
        var email = ApiClient.UniqueEmail();
        var device1 = await (await _client.RegisterAsync(email)).ReadTokensAsync();
        var device2 = await (await _client.LoginAsync(email)).ReadTokensAsync();

        var logoutAll = await _client.LogoutAllAsync(device1.AccessToken);
        logoutAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The token-version check rejects the still-unexpired access token on
        // its very next use - this is the per-request DB check working.
        (await _client.LogoutAllAsync(device1.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Every device's refresh token is gone too.
        await (await _client.RefreshAsync(device1.RefreshToken))
            .ShouldBeProblemAsync(401, "refresh_token.invalid");
        await (await _client.RefreshAsync(device2.RefreshToken))
            .ShouldBeProblemAsync(401, "refresh_token.invalid");
    }

    [Fact]
    public async Task Logout_all_demands_authentication()
    {
        (await _client.LogoutAllAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.LogoutAllAsync("garbage.jwt.value")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
