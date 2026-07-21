using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Account erasure end to end: the request is accepted synchronously, and the
/// deletion fans out through the event bus and removes the account and its
/// sessions shortly after.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountErasureTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Deleting_the_account_is_accepted_with_204()
    {
        var tokens = await _client.RegisterNewUserAsync();

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Deleting_demands_authentication()
    {
        (await _client.DeleteAccountAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.DeleteAccountAsync("garbage.jwt.value")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Erasure_removes_the_account_and_its_sessions()
    {
        var tokens = await _client.RegisterNewUserAsync();

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The fan-out completes shortly after acceptance: Identity's own handler
        // deletes the user, at which point the still-unexpired access token stops
        // authenticating - the per-request token-version check finds no account.
        await Poll.UntilAsync(
            async () => (await _client.GetAccountAsync(tokens.AccessToken)).StatusCode
                == HttpStatusCode.Unauthorized,
            "the erased account's access token should stop authenticating",
            TestContext.Current.CancellationToken);

        // The refresh tokens went with the user (FK cascade): no new session can
        // be minted from the erased account.
        await (await _client.RefreshAsync(tokens.RefreshToken))
            .ShouldBeProblemAsync(401, "refresh_token.invalid");
    }
}
