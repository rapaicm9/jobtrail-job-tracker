using System.Net;
using System.Text.Json;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// What a pipeline move announces to the rest of the system, read off the real
/// outbox after a real request. The claims under test: every accepted move is
/// announced, a move that closes an application or reopens one says so as its own
/// fact, a refused move announces nothing at all, and no announcement carries the
/// user's own account of their job search.
/// <para>
/// The rows are written in the request's own transaction, so they are there the
/// moment the response is - no waiting, except where delivery itself is the claim.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApplicationEventTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Announces_an_ordinary_advance_as_a_stage_change_alone()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        await MoveAsync(tokens.AccessToken, applicationId, "Screening");

        (await EventsAfterCreationAsync(applicationId))
            .ShouldBe([ApplicationStageChanged.EventType]);
    }

    [Fact]
    public async Task Announces_a_closed_application_as_reaching_a_terminal()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        await MoveAsync(tokens.AccessToken, applicationId, "Rejected");

        // Both rows are written in one transaction and share an occurred-at, so
        // they may be claimed in either order. The set is the claim, not a sequence.
        (await EventsAfterCreationAsync(applicationId)).ShouldBe(
            [ApplicationStageChanged.EventType, ApplicationReachedTerminal.EventType],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Announces_a_corrected_outcome_as_reaching_a_terminal_too()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        await MoveAsync(tokens.AccessToken, applicationId, "Ghosted");

        // The ghost that finally sends the rejection: still terminal, still worth
        // announcing - a consumer that filed it under Ghosted has to hear about it.
        await MoveAsync(tokens.AccessToken, applicationId, "Rejected");

        var terminals = await fixture.MessagesForAsync(applicationId, Ct);
        terminals.Count(message => message.EventType == ApplicationReachedTerminal.EventType).ShouldBe(2);
    }

    [Fact]
    public async Task Announces_a_revived_application_as_reopened()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        await MoveAsync(tokens.AccessToken, applicationId, "Rejected");

        await MoveAsync(tokens.AccessToken, applicationId, "Screening");

        (await EventsAfterCreationAsync(applicationId)).ShouldContain(ApplicationReopened.EventType);
    }

    [Fact]
    public async Task Announces_nothing_when_the_move_is_refused()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        // Applied is where it already is, and a same-stage move is no transition.
        var response = await _client.TransitionApplicationAsync(tokens.AccessToken, applicationId, "Applied");
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await EventsAfterCreationAsync(applicationId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Announces_nothing_when_the_application_is_someone_elses()
    {
        var (_, applicationId) = await AnApplicationAsync();
        var stranger = await fixture.RegisterWithDefaultCampaignAsync(fixture.CreateClient(), Ct);

        var response = await _client.TransitionApplicationAsync(stranger.AccessToken, applicationId, "Screening");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await EventsAfterCreationAsync(applicationId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Carries_both_ends_of_the_move_and_none_of_the_users_own_words()
    {
        var (tokens, applicationId) = await AnApplicationAsync(role: "Staff Engineer");

        await MoveAsync(tokens.AccessToken, applicationId, "Interview");

        var message = await fixture.SingleMessageForAsync(
            applicationId, ApplicationStageChanged.EventType, Ct);

        // Where it came from as well as where it went: the event states the whole
        // move, so a consumer can apply it without having seen the one before.
        message.Payload.ShouldContain("Applied");
        message.Payload.ShouldContain("Interview");
        message.Payload.ShouldContain(tokens.UserId.ToString());
        message.Payload.ShouldNotContain("Staff Engineer");
    }

    [Fact]
    public async Task Gives_every_announcement_an_identity_of_its_own()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        await MoveAsync(tokens.AccessToken, applicationId, "Rejected");

        // At-least-once delivery means a handler may see one of these twice; the
        // id is what lets it tell a repeat from the other event of the same move.
        var identities = (await fixture.MessagesForAsync(applicationId, Ct))
            .Select(message => EventIdOf(message.Payload))
            .ToList();

        identities.ShouldBeUnique();
        identities.ShouldNotContain(Guid.Empty);
    }

    [Fact]
    public async Task Delivers_what_a_move_announced()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        await MoveAsync(tokens.AccessToken, applicationId, "Rejected");

        // The registration is only real if the running host can turn these names
        // back into events: an unregistered one is retried and parked instead.
        await Poll.UntilAsync(
            async () => (await fixture.MessagesForAsync(applicationId, Ct))
                .All(message => message.ProcessedAt is not null),
            "the dispatcher should deliver everything the move announced",
            Ct);
    }

    private async Task<(AuthTokens Tokens, Guid ApplicationId)> AnApplicationAsync(string role = "Engineer")
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role })).ReadApplicationAsync();

        return (tokens, created.Id);
    }

    private async Task MoveAsync(string? accessToken, Guid applicationId, string targetStage)
    {
        var response = await _client.TransitionApplicationAsync(accessToken, applicationId, targetStage);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// What the application's moves announced, with the creation event dropped -
    /// every one of these applications was submitted, and that is another box's claim.
    /// </summary>
    private async Task<IReadOnlyList<string>> EventsAfterCreationAsync(Guid applicationId) =>
        [.. (await fixture.EventTypesForAsync(applicationId, Ct))
            .Where(eventType => eventType != ApplicationSubmitted.EventType)];

    private static Guid EventIdOf(string payload) =>
        JsonDocument.Parse(payload).RootElement.GetProperty("EventId").GetGuid();
}
