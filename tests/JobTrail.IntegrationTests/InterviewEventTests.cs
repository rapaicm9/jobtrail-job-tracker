using System.Net;
using System.Text.Json;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Contracts;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// What an interview round announces across its life, read off the real outbox.
/// A round has no delete, so its outcome is the only way a user takes one off the
/// calendar - which makes the claims here about whether the round is still
/// <em>awaited</em>: scheduling one says so, moving it says so again, calling it
/// off retracts it, and putting a cancelled one back says so once more. Recording
/// that a round happened retracts nothing, because its instant has passed.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InterviewEventTests(ApiFixture fixture)
{
    private static readonly DateTimeOffset Scheduled = new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Moved = new(2026, 8, 14, 15, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Announces_a_newly_scheduled_round_by_its_own_id()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);

        var message = await fixture.SingleMessageForAsync(interview.Id, InterviewScheduled.EventType, Ct);

        // The id the reminders will be keyed under has to be the round's real one.
        // It is generated in the handler for exactly this reason - left to the
        // database it would still be empty when the event was written.
        GuidOf(message.Payload, "InterviewId").ShouldBe(interview.Id);
        GuidOf(message.Payload, "ApplicationId").ShouldBe(applicationId);
        message.Payload.ShouldContain(tokens.UserId.ToString());
    }

    [Fact]
    public async Task Announces_a_round_moved_to_a_new_time()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);

        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Moved, "Pending");

        // The same round, announced again: a consumer keyed on the interview id
        // replaces what it holds, so no separate "rescheduled" event is needed.
        (await EventsForRoundAsync(interview.Id))
            .ShouldBe([InterviewScheduled.EventType, InterviewScheduled.EventType]);
    }

    [Fact]
    public async Task Says_nothing_when_an_edit_leaves_the_time_and_the_outcome_alone()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);

        // Only the notes moved, and nobody schedules a reminder off those.
        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Scheduled, "Pending", "bring examples");

        (await EventsForRoundAsync(interview.Id)).ShouldBe([InterviewScheduled.EventType]);
    }

    [Fact]
    public async Task Announces_a_round_that_was_called_off()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);

        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Scheduled, "Cancelled");

        (await EventsForRoundAsync(interview.Id))
            .ShouldBe([InterviewScheduled.EventType, InterviewCancelled.EventType]);
    }

    [Fact]
    public async Task Announces_a_cancelled_round_that_is_back_on()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);
        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Scheduled, "Cancelled");

        // Cancelled by mistake and put back at the same time. Nothing about the
        // time changed, so only the outcome says the round is awaited again - miss
        // that and the reminder stays dropped.
        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Scheduled, "Pending");

        (await EventsForRoundAsync(interview.Id)).ShouldBe(
            [InterviewScheduled.EventType, InterviewCancelled.EventType, InterviewScheduled.EventType]);
    }

    [Fact]
    public async Task Retracts_nothing_when_a_round_that_happened_is_recorded()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);

        // An outcome is entered after the round; the instant a reminder was armed
        // for has passed, so there is nothing left to call off.
        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Scheduled, "Passed");

        (await EventsForRoundAsync(interview.Id)).ShouldBe([InterviewScheduled.EventType]);
    }

    [Fact]
    public async Task Announces_nothing_when_the_round_is_someone_elses()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);
        var stranger = await fixture.RegisterWithDefaultCampaignAsync(fixture.CreateClient(), Ct);

        var response = await _client.UpdateInterviewAsync(
            stranger.AccessToken, applicationId, interview.Id, Round(Moved, "Cancelled"));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await EventsForRoundAsync(interview.Id)).ShouldBe([InterviewScheduled.EventType]);
    }

    [Fact]
    public async Task Carries_the_round_and_none_of_its_notes()
    {
        var (tokens, applicationId) = await AnApplicationAsync();

        var interview = await ScheduleAsync(tokens.AccessToken, applicationId, notes: "ask about the on-call rota");

        var message = await fixture.SingleMessageForAsync(interview.Id, InterviewScheduled.EventType, Ct);
        message.Payload.ShouldContain("2026-08-12");
        message.Payload.ShouldNotContain("on-call rota");
    }

    [Fact]
    public async Task Delivers_what_a_round_announced()
    {
        var (tokens, applicationId) = await AnApplicationAsync();
        var interview = await ScheduleAsync(tokens.AccessToken, applicationId);

        await ReplaceAsync(tokens.AccessToken, applicationId, interview.Id, Scheduled, "Cancelled");

        // Both names have to be ones the running host can turn back into events;
        // an unregistered one would be retried and parked instead.
        await Poll.UntilAsync(
            async () => (await fixture.MessagesForAsync(interview.Id, Ct))
                .All(message => message.ProcessedAt is not null),
            "the dispatcher should deliver everything the round announced",
            Ct);
    }

    private async Task<(AuthTokens Tokens, Guid ApplicationId)> AnApplicationAsync()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        return (tokens, created.Id);
    }

    private async Task<InterviewView> ScheduleAsync(string? accessToken, Guid applicationId, string? notes = null) =>
        await (await _client.CreateInterviewAsync(accessToken, applicationId, new
        {
            scheduledAt = Scheduled,
            type = "Technical",
            format = "Remote",
            notes,
        })).ReadInterviewAsync();

    private async Task ReplaceAsync(
        string? accessToken,
        Guid applicationId,
        Guid interviewId,
        DateTimeOffset scheduledAt,
        string outcome,
        string? notes = null)
    {
        var response = await _client.UpdateInterviewAsync(
            accessToken, applicationId, interviewId, Round(scheduledAt, outcome, notes));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static object Round(DateTimeOffset scheduledAt, string outcome, string? notes = null) => new
    {
        scheduledAt,
        type = "Technical",
        format = "Remote",
        outcome,
        notes,
    };

    /// <summary>
    /// What was announced about this round, in the order it was recorded. Each of
    /// these lands in its own request, so unlike events written together these do
    /// have an order worth asserting on.
    /// </summary>
    private async Task<IReadOnlyList<string>> EventsForRoundAsync(Guid interviewId) =>
        await fixture.EventTypesForAsync(interviewId, Ct);

    private static Guid GuidOf(string payload, string property) =>
        JsonDocument.Parse(payload).RootElement.GetProperty(property).GetGuid();
}
