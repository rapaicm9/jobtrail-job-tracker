using JobTrail.Infrastructure.Outbox;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
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
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OutboxTests(ApiFixture fixture)
{
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

        var message = await FindMessageAsync(created.Id);
        message.EventType.ShouldBe(ApplicationSubmitted.EventType);

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

        (await MessagesForOwnerAsync(tokens.UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Delivers_the_event_and_marks_the_row_processed()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await Poll.UntilAsync(
            async () => (await FindMessageAsync(created.Id)).ProcessedAt is not null,
            "the dispatcher should deliver the recorded event and mark it processed",
            Ct);

        var message = await FindMessageAsync(created.Id);
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
            async () => (await FindMessageAsync(created.Id)).ProcessedAt is not null,
            "the event should be delivered before the row is watched for a second delivery",
            Ct);
        var delivered = await FindMessageAsync(created.Id);

        // Several poll intervals: a row that was going to be claimed again would
        // have been by now.
        await Task.Delay(TimeSpan.FromMilliseconds(600), Ct);

        var later = await FindMessageAsync(created.Id);
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

    /// <summary>Records a row whose event name nothing is registered under, so delivery cannot succeed.</summary>
    private async Task<Guid> RecordUndeliverableAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var message = OutboxMessage.For(
            "applications.never_registered",
            new ApplicationSubmitted(
                Guid.CreateVersion7(), ownerId, Guid.CreateVersion7(), null,
                new DateOnly(2026, 7, 24), null, null, DateTimeOffset.UtcNow));

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

    private async Task<OutboxMessage> FindMessageAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var messages = await dbContext.Outbox.AsNoTracking().ToListAsync(Ct);
        return messages
            .Where(m => m.Payload.Contains(applicationId.ToString(), StringComparison.Ordinal))
            .ShouldHaveSingleItem();
    }

    private async Task<IReadOnlyList<OutboxMessage>> MessagesForOwnerAsync(Guid ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var messages = await dbContext.Outbox.AsNoTracking().ToListAsync(Ct);
        return [.. messages.Where(m => m.Payload.Contains(ownerId.ToString(), StringComparison.Ordinal))];
    }
}
