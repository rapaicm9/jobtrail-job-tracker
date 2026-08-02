using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Account erasure end to end: the request is accepted synchronously, recorded
/// durably in the same breath, and carried out across every module shortly after.
/// <para>
/// The recording is the part worth testing hardest. The 204 is a promise made to
/// the user, and the only thing that keeps it across a restart is the request
/// being in the database before the response is written - not in a queue in
/// memory, where a stopped host takes it and erases nothing at all.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountErasureTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public async Task The_request_is_recorded_before_the_204_is_written()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // No polling: by the time the response is in hand the row is committed, or
        // the promise the 204 makes is one a restart could break.
        var request = (await fixture.ErasureRequestsForAsync(userId, Ct)).ShouldHaveSingleItem();
        request.Payload.ShouldContain(tokens.UserId.ToString());
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
            Ct);

        // The refresh tokens went with the user (FK cascade): no new session can
        // be minted from the erased account.
        await (await _client.RefreshAsync(tokens.RefreshToken))
            .ShouldBeProblemAsync(401, "refresh_token.invalid");
    }

    [Fact]
    public async Task Erasure_reaches_every_module_that_holds_the_user()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var userId = UserId.From(tokens.UserId);

        await (await _client.CreateApplicationAsync(tokens.AccessToken, new { role = "Staff Engineer" }))
            .ReadApplicationAsync();

        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(userId, Ct) is not null,
            "registration should provision the plan the erasure removes",
            Ct);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // One request, three modules, each erasing its own schema and none reading
        // another's. Asserted together rather than module by module because the
        // claim is that the whole fan-out lands, not that any one handler works.
        await Poll.UntilAsync(
            async () =>
                !await fixture.UserExistsAsync(userId, Ct)
                && await fixture.PlanForAsync(userId, Ct) is null
                && !await fixture.HasDefaultCampaignAsync(userId, Ct),
            "deleting the account should erase the user from Identity, Billing and Applications",
            Ct);
    }

    [Fact]
    public async Task The_erasure_request_outlives_the_erasure_it_asked_for()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Poll.UntilAsync(
            async () => (await fixture.ErasureRequestsForAsync(userId, Ct))
                .Any(request => request.ProcessedAt is not null),
            "the dispatcher should deliver the erasure request and mark it processed",
            Ct);

        // Deliberately not erased with everything else it carries the owner of.
        // The row is the record that the request was made and carried out, it is
        // the row the dispatcher is holding while the handlers run, and deleting it
        // from inside one of those handlers would strand the delivery that is
        // mid-flight. It goes when the outbox is pruned, like every other.
        var delivered = (await fixture.ErasureRequestsForAsync(userId, Ct)).ShouldHaveSingleItem();
        delivered.ProcessedAt.ShouldNotBeNull();
        delivered.Error.ShouldBeNull();
    }
}
