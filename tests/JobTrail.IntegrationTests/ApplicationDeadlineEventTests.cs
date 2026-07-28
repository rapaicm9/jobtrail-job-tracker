using System.Globalization;
using System.Net;
using System.Text.Json;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Contracts;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// What a date on an application announces, read off the real outbox. These are
/// the events reminders are built from, so the claims are about when one is owed:
/// a deadline entered at creation counts, a replace that moves one counts, a
/// replace that leaves it alone does not, and clearing one is announced too -
/// otherwise a consumer keeps a reminder for a date that no longer exists.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApplicationDeadlineEventTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Announces_a_deadline_entered_while_opening_the_application()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var created = await CreateAsync(
            tokens.AccessToken, new { role = "Engineer", applicationDeadline = "2026-08-05" });

        (await fixture.EventTypesForAsync(created.Id, Ct)).ShouldBe(
            [ApplicationSubmitted.EventType, ApplicationDeadlineSet.EventType],
            ignoreOrder: true);

        var message = await fixture.SingleMessageForAsync(created.Id, ApplicationDeadlineSet.EventType, Ct);
        DeadlineOf(message.Payload).ShouldBe(new DateOnly(2026, 8, 5));
    }

    [Fact]
    public async Task Announces_nothing_extra_when_the_application_has_no_deadline()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });

        (await fixture.EventTypesForAsync(created.Id, Ct)).ShouldBe([ApplicationSubmitted.EventType]);
    }

    [Fact]
    public async Task Announces_a_deadline_a_replace_moved()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });

        await ReplaceAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = created.CampaignId,
            appliedDate = created.AppliedDate.ToString("O"),
            applicationDeadline = "2026-09-01",
        });

        var message = await fixture.SingleMessageForAsync(created.Id, ApplicationDeadlineSet.EventType, Ct);
        DeadlineOf(message.Payload).ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Says_nothing_when_a_replace_leaves_the_deadline_where_it_was()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(
            tokens.AccessToken, new { role = "Engineer", applicationDeadline = "2026-08-05" });

        // A full replace invites a client to send back the record it already has.
        // Re-announcing that would have Notifications rescheduling on every edit.
        await ReplaceAsync(tokens.AccessToken, created.Id, new
        {
            role = "Senior Engineer",
            campaignId = created.CampaignId,
            appliedDate = created.AppliedDate.ToString("O"),
            applicationDeadline = "2026-08-05",
        });

        (await fixture.MessagesForAsync(created.Id, Ct))
            .Count(message => message.EventType == ApplicationDeadlineSet.EventType)
            .ShouldBe(1);
    }

    [Fact]
    public async Task Announces_a_deadline_that_was_cleared()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(
            tokens.AccessToken, new { role = "Engineer", applicationDeadline = "2026-08-05" });

        // Left off a replace, so it is gone - and the event has to say so, or a
        // reminder stays armed for a date the user deleted.
        await ReplaceAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = created.CampaignId,
            appliedDate = created.AppliedDate.ToString("O"),
        });

        var deadlines = (await fixture.MessagesForAsync(created.Id, Ct))
            .Where(message => message.EventType == ApplicationDeadlineSet.EventType)
            .Select(message => DeadlineOf(message.Payload));

        // One for the date it was opened with, one saying that date is gone.
        deadlines.ShouldBe([new DateOnly(2026, 8, 5), null], ignoreOrder: true);
    }

    [Fact]
    public async Task Announces_an_offer_decision_deadline_once_there_is_an_offer()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });
        await MoveToOfferAsync(tokens.AccessToken, created.Id);

        await ReplaceAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = created.CampaignId,
            appliedDate = created.AppliedDate.ToString("O"),
            offerDecisionDeadline = "2026-09-15",
        });

        var message = await fixture.SingleMessageForAsync(created.Id, OfferDecisionDeadlineSet.EventType, Ct);
        DeadlineOf(message.Payload).ShouldBe(new DateOnly(2026, 9, 15));
        message.Payload.ShouldContain(tokens.UserId.ToString());
    }

    [Fact]
    public async Task Announces_no_offer_decision_deadline_when_the_replace_is_refused()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });

        // No offer yet, so the deadline is meaningless and the whole replace is
        // refused - which means there is nothing to announce either.
        var response = await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = created.CampaignId,
            appliedDate = created.AppliedDate.ToString("O"),
            offerDecisionDeadline = "2026-09-15",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await fixture.EventTypesForAsync(created.Id, Ct))
            .ShouldNotContain(OfferDecisionDeadlineSet.EventType);
    }

    [Fact]
    public async Task Announces_nothing_when_the_application_is_someone_elses()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });
        var stranger = await fixture.RegisterWithDefaultCampaignAsync(fixture.CreateClient(), Ct);

        var response = await _client.UpdateApplicationAsync(stranger.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = created.CampaignId,
            appliedDate = created.AppliedDate.ToString("O"),
            applicationDeadline = "2026-09-01",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await fixture.EventTypesForAsync(created.Id, Ct))
            .ShouldNotContain(ApplicationDeadlineSet.EventType);
    }

    [Fact]
    public async Task Carries_the_date_and_none_of_the_users_own_words()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var created = await CreateAsync(tokens.AccessToken, new
        {
            role = "Staff Engineer",
            companyName = "Acme Corp",
            applicationDeadline = "2026-08-05",
        });

        var message = await fixture.SingleMessageForAsync(created.Id, ApplicationDeadlineSet.EventType, Ct);

        // A reminder needs the owner, the application and the date. It does not
        // need what the job is or who it is with.
        message.Payload.ShouldContain(tokens.UserId.ToString());
        message.Payload.ShouldContain("2026-08-05");
        message.Payload.ShouldNotContain("Staff Engineer");
        message.Payload.ShouldNotContain("Acme Corp");
    }

    private async Task<ApplicationView> CreateAsync(string? accessToken, object body) =>
        await (await _client.CreateApplicationAsync(accessToken, body)).ReadApplicationAsync();

    private async Task ReplaceAsync(string? accessToken, Guid id, object body)
    {
        var response = await _client.UpdateApplicationAsync(accessToken, id, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Walks the application up to Offer, the one stage an offer-decision deadline is allowed at.</summary>
    private async Task MoveToOfferAsync(string? accessToken, Guid id)
    {
        var response = await _client.TransitionApplicationAsync(accessToken, id, "Offer");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static DateOnly? DeadlineOf(string payload)
    {
        var deadline = JsonDocument.Parse(payload).RootElement.GetProperty("Deadline");
        return deadline.ValueKind is JsonValueKind.Null
            ? null
            : DateOnly.Parse(deadline.GetString()!, CultureInfo.InvariantCulture);
    }
}
