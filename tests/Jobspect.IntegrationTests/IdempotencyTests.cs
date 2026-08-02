using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobspect.Api.Idempotency;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// A retried mutation happening once, proved against the real Redis cache. The
/// claims: a replayed key returns the first response rather than causing a second
/// one - no second row, and no second integration event - a key used for a
/// different request is refused, keys are the caller's own, and a request that
/// failed leaves its key free to try again.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdempotencyTests(ApiFixture fixture)
{
    private const string Applications = "/api/v1/applications";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Replays_the_first_response_instead_of_creating_twice()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var key = NewKey();
        var body = new { role = "Staff Engineer" };

        var first = await PostAsync(Applications, tokens.AccessToken, body, key);
        var second = await PostAsync(Applications, tokens.AccessToken, body, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues(IdempotencyMiddleware.ReplayedHeaderName).ShouldBe(["true"]);

        // The same application, byte for byte - including its id and its Location.
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe(await first.Content.ReadAsStringAsync(Ct));
        second.Headers.Location.ShouldBe(first.Headers.Location);

        // And it happened once: one row, and one announcement of it.
        var applications = await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync();
        applications.ShouldHaveSingleItem();

        var announced = await fixture.MessagesForAsync(tokens.UserId, Ct);
        announced.Count(message => message.EventType == ApplicationSubmitted.EventType).ShouldBe(1);
    }

    [Fact]
    public async Task Replays_a_transition_rather_than_refusing_it_as_an_illegal_move()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);
        var key = NewKey();
        var route = $"{Applications}/{application.Id}/transition";

        var first = await PostAsync(route, tokens.AccessToken, new { targetStage = "Screening" }, key);
        var second = await PostAsync(route, tokens.AccessToken, new { targetStage = "Screening" }, key);

        // Without the key the second call is a 422: the application has already
        // moved, and Screening→Screening is no transition. That is a bewildering
        // answer to a retry, and the key is what turns it back into the first one.
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe(await first.Content.ReadAsStringAsync(Ct));

        var announced = await fixture.MessagesForAsync(application.Id, Ct);
        announced.Count(message => message.EventType == ApplicationStageChanged.EventType).ShouldBe(1);
    }

    [Fact]
    public async Task Runs_again_without_a_key_or_under_a_different_one()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var body = new { role = "Engineer" };

        await PostAsync(Applications, tokens.AccessToken, body, NewKey());
        await PostAsync(Applications, tokens.AccessToken, body, NewKey());
        await PostAsync(Applications, tokens.AccessToken, body);

        // Keys are opt-in and each one is its own operation: three requests, three
        // applications.
        var applications = await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync();
        applications.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Refuses_a_key_already_used_for_a_different_request()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var key = NewKey();

        await PostAsync(Applications, tokens.AccessToken, new { role = "Engineer" }, key);
        var reused = await PostAsync(Applications, tokens.AccessToken, new { role = "Designer" }, key);

        // Answering this with the first application would silently swallow the
        // second one - a client bug that would otherwise look like data loss.
        await reused.ShouldBeProblemAsync((int)HttpStatusCode.UnprocessableEntity, "idempotency.key_reused");
    }

    [Fact]
    public async Task Keeps_one_callers_keys_away_from_another()
    {
        var key = NewKey();
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(fixture.CreateClient(), Ct);

        var first = await PostAsync(Applications, mine.AccessToken, new { role = "Engineer" }, key);
        var second = await PostAsync(Applications, theirs.AccessToken, new { role = "Engineer" }, key);

        // Same key, same body, different caller: an independent application, and
        // no sight of the other user's response.
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).ShouldBeFalse();
        (await second.Content.ReadAsStringAsync(Ct)).ShouldNotBe(await first.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Refuses_a_key_whose_first_request_is_still_running()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var key = NewKey();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { role = "Engineer" });

        // Reserved and not completed - what the cache holds while a first request
        // is in flight. Seeded directly, so the race is a fact rather than a matter
        // of timing, and under the fingerprint of the request that follows: the
        // same operation arriving twice at once, not a key used for two things.
        await ReserveAsync(
            UserId.From(tokens.UserId), key, IdempotencyFingerprint.Of("POST", Applications, payload));

        var response = await _client.SendContentWithKeyAsync(
            HttpMethod.Post, Applications, tokens.AccessToken, JsonBody(payload), key);

        await response.ShouldBeProblemAsync((int)HttpStatusCode.Conflict, "idempotency.in_flight");
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Refuses_a_running_key_claimed_for_something_else_outright()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var key = NewKey();

        await ReserveAsync(UserId.From(tokens.UserId), key, "the fingerprint of another request");

        // Still in flight, but demonstrably a different operation. Waiting for the
        // first one to finish would never help, so this is the flat refusal rather
        // than the try-again-shortly one.
        var response = await PostAsync(Applications, tokens.AccessToken, new { role = "Engineer" }, key);

        await response.ShouldBeProblemAsync((int)HttpStatusCode.UnprocessableEntity, "idempotency.key_reused");
    }

    [Fact]
    public async Task Leaves_a_key_usable_after_the_request_was_rejected()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var key = NewKey();

        // No role: a 422, and nothing written. The key must not be spent on it, or
        // the client could never correct the body and send it again.
        var rejected = await PostAsync(Applications, tokens.AccessToken, new { source = "LinkedIn" }, key);
        rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var corrected = await PostAsync(Applications, tokens.AccessToken, new { role = "Engineer" }, key);
        corrected.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Treats_an_empty_key_as_no_key_at_all()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // An empty header value does not survive the wire, so the server cannot
        // tell it from an absent one. The request simply runs, unkeyed - asserted
        // rather than assumed, because the alternative would be a validation rule
        // that can never fire.
        var response = await PostAsync(Applications, tokens.AccessToken, new { role = "Engineer" }, string.Empty);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadRecordAsync(UserId.From(tokens.UserId), string.Empty)).ShouldBeNull();
    }

    [Theory]
    [InlineData("key\twith\ttabs")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Refuses_a_malformed_key(string key)
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await PostAsync(Applications, tokens.AccessToken, new { role = "Engineer" }, key);

        await response.ShouldBeProblemAsync((int)HttpStatusCode.UnprocessableEntity, "idempotency.key_invalid");
    }

    [Fact]
    public async Task Keeps_tokens_out_of_the_cache()
    {
        var email = ApiClient.UniqueEmail();
        await _client.RegisterAsync(email);
        var key = NewKey();

        var first = await LoginWithKeyAsync(email, key);
        var second = await LoginWithKeyAsync(email, key);

        // The auth surface is outside this entirely: it is unauthenticated, so
        // there is no caller to scope a key by, and its responses are token pairs
        // that have no business sitting in a cache for a day. Two logins, two
        // distinct pairs, nothing replayed.
        var mine = await first.ReadTokensAsync();
        var theirs = await second.ReadTokensAsync();
        theirs.RefreshToken.ShouldNotBe(mine.RefreshToken);
        second.Headers.Contains(IdempotencyMiddleware.ReplayedHeaderName).ShouldBeFalse();
    }

    [Fact]
    public async Task Ignores_a_key_on_a_read()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var key = NewKey();

        var response = await _client.SendWithKeyAsync(
            HttpMethod.Get, Applications, tokens.AccessToken, idempotencyKey: key);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadRecordAsync(UserId.From(tokens.UserId), key)).ShouldBeNull();
    }

    private static string NewKey() => Guid.CreateVersion7().ToString("N");

    private Task<HttpResponseMessage> PostAsync(
        string uri, string? accessToken, object body, string? idempotencyKey = null) =>
        _client.SendWithKeyAsync(HttpMethod.Post, uri, accessToken, body, idempotencyKey);

    private Task<HttpResponseMessage> LoginWithKeyAsync(string email, string key) =>
        _client.SendWithKeyAsync(
            HttpMethod.Post,
            "/api/v1/identity/login",
            accessToken: null,
            new { email, password = ApiClient.Password },
            key);

    private async Task<ApplicationView> CreateApplicationAsync(string? accessToken) =>
        await (await _client.CreateApplicationAsync(accessToken, new { role = "Engineer" })).ReadApplicationAsync();

    private static ByteArrayContent JsonBody(byte[] payload) =>
        new(payload) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };

    private async Task ReserveAsync(UserId owner, string key, string fingerprint)
    {
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IdempotencyStore>();

        (await store.TryReserveAsync(owner, key, IdempotencyRecord.Reserve(fingerprint))).ShouldBeTrue();
    }

    private async Task<IdempotencyRecord?> ReadRecordAsync(UserId owner, string key)
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdempotencyStore>().ReadAsync(owner, key);
    }
}
