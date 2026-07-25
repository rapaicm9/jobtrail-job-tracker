using JobTrail.Infrastructure.Outbox;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Durable event delivery against a real database and the real dispatcher. The
/// claims under test: creating an application records its event in the same write,
/// a rejected create records nothing, the dispatcher delivers and marks the row
/// processed exactly once, and an event it cannot deliver stays owed rather than
/// disappearing.
/// <para>
/// And what happens after that failure, which is the half that makes the
/// machinery worth having: the event is retried rather than abandoned, it waits
/// out its backoff instead of being hammered every tick, and past the attempt
/// ceiling it is left alone - unprocessed and visible, which is the state an
/// operator needs to find.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OutboxTests(ApiFixture fixture)
{
    /// <summary>The dispatcher's ceiling, past which a row is left for a human.</summary>
    private const int MaxAttempts = 10;

    /// <summary>More rows than one claim takes, so the drain has to come round again.</summary>
    private const int BurstSize = 25;

    /// <summary>
    /// Long enough for several poll ticks - the test host polls every 150ms - so a
    /// row that was going to be claimed again would have been by now.
    /// </summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Records_the_event_when_the_application_is_created()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var created = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Staff Engineer",
            source = "LinkedIn",
            workMode = "Hybrid",
            appliedDate = "2026-07-20",
        })).ReadApplicationAsync();

        var message = await SubmittedMessageAsync(created.Id);

        // The payload carries what a consumer cannot look up for itself, and none
        // of the user's own account of the role.
        message.Payload.ShouldContain(created.Id.ToString());
        message.Payload.ShouldContain(tokens.UserId.ToString());
        message.Payload.ShouldContain("LinkedIn");
        message.Payload.ShouldContain("Hybrid");
        message.Payload.ShouldNotContain("Staff Engineer");
    }

    [Fact]
    public async Task Records_nothing_when_the_create_is_rejected()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // No role: the write never happens, so neither does the announcement.
        var response = await _client.CreateApplicationAsync(tokens.AccessToken, new { source = "LinkedIn" });
        await response.ShouldBeValidationProblemAsync("role");

        (await fixture.MessagesForAsync(tokens.UserId, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Delivers_the_event_and_marks_the_row_processed()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await Poll.UntilAsync(
            async () => (await SubmittedMessageAsync(created.Id)).ProcessedAt is not null,
            "the dispatcher should deliver the recorded event and mark it processed",
            Ct);

        var message = await SubmittedMessageAsync(created.Id);
        message.Attempts.ShouldBe(0);
        message.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Leaves_a_processed_row_alone()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await Poll.UntilAsync(
            async () => (await SubmittedMessageAsync(created.Id)).ProcessedAt is not null,
            "the event should be delivered before the row is watched for a second delivery",
            Ct);
        var delivered = await SubmittedMessageAsync(created.Id);

        await Task.Delay(Quiet, Ct);

        var later = await SubmittedMessageAsync(created.Id);
        later.ProcessedAt.ShouldBe(delivered.ProcessedAt);
        later.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task Keeps_owing_an_event_it_cannot_deliver()
    {
        var ownerId = UserId.New();
        var messageId = await RecordUndeliverableAsync(ownerId);

        await Poll.UntilAsync(
            async () => (await GetMessageAsync(messageId)).Attempts > 0,
            "the dispatcher should record the failed delivery attempt",
            Ct);

        var message = await GetMessageAsync(messageId);

        // Still owed: unprocessed, with a reason and a time to try again. An event
        // nobody can deliver must stay visible rather than vanish.
        message.ProcessedAt.ShouldBeNull();
        message.Error.ShouldNotBeNull();
        message.NextAttemptAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Retries_a_failed_event_until_it_goes_through()
    {
        var messageId = await RecordUndeliverableAsync(UserId.New());

        await Poll.UntilAsync(
            async () => (await GetMessageAsync(messageId)).Attempts > 0,
            "the dispatcher should fail to deliver the event at least once",
            Ct);

        // Whatever was wrong is now right - the deployment that could not read
        // this event can. Nothing else changes: the same row, still owed.
        await MakeDeliverableAsync(messageId);

        await Poll.UntilAsync(
            async () => (await GetMessageAsync(messageId)).ProcessedAt is not null,
            "the dispatcher should come back to an event that failed and deliver it",
            Ct);

        var message = await GetMessageAsync(messageId);

        // The failure is still counted - it happened - but the reason and the
        // retry time are cleared, because nothing is owed any more.
        message.Attempts.ShouldBeGreaterThan(0);
        message.Error.ShouldBeNull();
        message.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public async Task Waits_out_the_backoff_rather_than_retrying_every_tick()
    {
        var messageId = await RecordUndeliverableAsync(UserId.New());

        await Poll.UntilAsync(
            async () => (await GetMessageAsync(messageId)).Attempts > 0,
            "the dispatcher should record the first failed attempt",
            Ct);

        var afterFirstFailure = await GetMessageAsync(messageId);
        var untilRetry = afterFirstFailure.NextAttemptAt!.Value - DateTimeOffset.UtcNow;

        // The first retry is seconds out, and the dispatcher polls several times a
        // second. Without the backoff a broken handler would be hammered.
        untilRetry.ShouldBeGreaterThan(
            Quiet, "the first retry should be far enough out to observe the dispatcher leaving the row alone");

        await Task.Delay(Quiet, Ct);

        (await GetMessageAsync(messageId)).Attempts.ShouldBe(afterFirstFailure.Attempts);
    }

    [Fact]
    public async Task Leaves_a_row_alone_once_it_has_run_out_of_attempts()
    {
        var messageId = await RecordUndeliverableAsync(UserId.New());

        // At the ceiling, and due now: the only thing keeping the dispatcher off
        // this row is the attempt limit itself.
        await ExhaustAttemptsAsync(messageId);
        await Task.Delay(Quiet, Ct);

        var message = await GetMessageAsync(messageId);

        // Not retried, and not swept away either. An event nobody can deliver has
        // to stay where a human will find it.
        message.Attempts.ShouldBe(MaxAttempts);
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Delivers_a_burst_larger_than_one_batch()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // A claim takes a bounded batch and the dispatcher keeps claiming until one
        // comes back short. Get that termination wrong and the tail is never
        // delivered - which nothing else here would notice.
        for (var index = 0; index < BurstSize; index++)
        {
            await (await _client.CreateApplicationAsync(tokens.AccessToken, new { role = $"Engineer {index}" }))
                .ReadApplicationAsync();
        }

        await Poll.UntilAsync(
            async () =>
            {
                var messages = await fixture.MessagesForAsync(tokens.UserId, Ct);
                return messages.Count == BurstSize && messages.All(message => message.ProcessedAt is not null);
            },
            $"the dispatcher should deliver all {BurstSize} events, not just the first batch",
            Ct);
    }

    /// <summary>Points the row at an event the module does register, leaving everything else about it alone.</summary>
    private async Task MakeDeliverableAsync(Guid messageId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE applications.outbox
            SET event_type = {ApplicationSubmitted.EventType}, next_attempt_at = NULL
            WHERE id = {messageId}
            """);
    }

    /// <summary>
    /// Spends the row's attempts outright. Waiting for ten real failures would mean
    /// waiting out nine backoffs; the state under test is the ceiling, not the
    /// journey to it.
    /// </summary>
    private async Task ExhaustAttemptsAsync(Guid messageId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE applications.outbox
            SET attempts = {MaxAttempts}, next_attempt_at = NULL
            WHERE id = {messageId}
            """);
    }

    /// <summary>Records a row whose event name nothing is registered under, so delivery cannot succeed.</summary>
    private async Task<Guid> RecordUndeliverableAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var message = OutboxMessage.For(new NeverRegistered(Guid.CreateVersion7(), ownerId));

        dbContext.Outbox.Add(message);
        await dbContext.SaveChangesAsync(Ct);

        return message.Id;
    }

    private async Task<OutboxMessage> GetMessageAsync(Guid id)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        return await dbContext.Outbox.AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }

    private Task<OutboxMessage> SubmittedMessageAsync(Guid applicationId) =>
        fixture.SingleMessageForAsync(applicationId, ApplicationSubmitted.EventType, Ct);

    /// <summary>
    /// An event the module never registers, so nothing can deliver it. Declared
    /// here rather than borrowing a real event under a made-up name: the name now
    /// belongs to the type, which is the point of the mechanism under test.
    /// </summary>
    private sealed record NeverRegistered(Guid EventId, UserId OwnerId) : IOutboxEvent
    {
        public static string EventType => "applications.never_registered";
    }
}
