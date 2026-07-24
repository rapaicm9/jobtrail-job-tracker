using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The interview slices under <c>/api/v1/applications/{id}/interviews</c> against a
/// real database: a round is scheduled pending and its outcome recorded on update,
/// the list is per-application and time-ordered, and ownership flows through the
/// parent application - a round on someone else's application, or under the wrong
/// one, is a 404.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InterviewEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Schedules_a_round_as_pending_and_returns_it()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.CreateInterviewAsync(tokens.AccessToken, appId, new
        {
            scheduledAt = "2026-08-01T14:00:00Z",
            type = "technical",
            format = "remote",
            notes = "Live coding.",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadInterviewAsync();
        created.ApplicationId.ShouldBe(appId);
        created.ScheduledAt.ShouldBe(new DateTimeOffset(2026, 8, 1, 14, 0, 0, TimeSpan.Zero));
        created.Type.ShouldBe("Technical");
        created.Format.ShouldBe("Remote");
        created.Outcome.ShouldBe("Pending");
        created.Notes.ShouldBe("Live coding.");
        response.Headers.Location!.ToString()
            .ShouldBe($"/api/v1/applications/{appId}/interviews/{created.Id}");

        var fetched = await (await _client.GetInterviewAsync(tokens.AccessToken, appId, created.Id)).ReadInterviewAsync();
        fetched.ShouldBe(created);
    }

    [Fact]
    public async Task Records_the_outcome_on_update()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);
        var created = await CreateInterviewAsync(tokens.AccessToken, appId);
        created.Outcome.ShouldBe("Pending");

        var updated = await (await _client.UpdateInterviewAsync(tokens.AccessToken, appId, created.Id, new
        {
            scheduledAt = "2026-08-02T09:30:00Z",
            type = "onsite",
            format = "onsite",
            outcome = "passed",
            notes = "Went well.",
        })).ReadInterviewAsync();

        updated.ScheduledAt.ShouldBe(new DateTimeOffset(2026, 8, 2, 9, 30, 0, TimeSpan.Zero));
        updated.Type.ShouldBe("Onsite");
        updated.Format.ShouldBe("Onsite");
        updated.Outcome.ShouldBe("Passed");
        updated.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Lists_rounds_earliest_scheduled_first()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);
        await CreateInterviewAsync(tokens.AccessToken, appId, "2026-08-10T10:00:00Z", notes: "Second");
        await CreateInterviewAsync(tokens.AccessToken, appId, "2026-08-01T10:00:00Z", notes: "First");
        await CreateInterviewAsync(tokens.AccessToken, appId, "2026-08-20T10:00:00Z", notes: "Third");

        var rounds = await (await _client.ListInterviewsAsync(tokens.AccessToken, appId)).ReadInterviewListAsync();

        rounds.Select(r => r.Notes).ShouldBe(["First", "Second", "Third"]);
    }

    [Fact]
    public async Task List_is_empty_for_an_application_with_no_rounds()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var rounds = await (await _client.ListInterviewsAsync(tokens.AccessToken, appId)).ReadInterviewListAsync();

        rounds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_requires_a_time_a_type_and_a_format()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.CreateInterviewAsync(tokens.AccessToken, appId, new { notes = "nothing else" });

        await response.ShouldBeValidationProblemAsync("scheduledAt", "type", "format");
    }

    [Fact]
    public async Task Create_rejects_an_unknown_type_or_format()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.CreateInterviewAsync(tokens.AccessToken, appId, new
        {
            scheduledAt = "2026-08-01T14:00:00Z",
            type = "Coffee",
            format = "Telepathy",
        });

        await response.ShouldBeValidationProblemAsync("type", "format");
    }

    [Fact]
    public async Task Update_requires_a_known_outcome()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);
        var created = await CreateInterviewAsync(tokens.AccessToken, appId);

        var response = await _client.UpdateInterviewAsync(tokens.AccessToken, appId, created.Id, new
        {
            scheduledAt = "2026-08-01T14:00:00Z",
            type = "technical",
            format = "remote",
            outcome = "Aced",
        });

        await response.ShouldBeValidationProblemAsync("outcome");
    }

    [Fact]
    public async Task Creating_under_another_users_application_is_404()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken);

        var response = await _client.CreateInterviewAsync(mine.AccessToken, theirApp, new
        {
            scheduledAt = "2026-08-01T14:00:00Z",
            type = "technical",
            format = "remote",
        });

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Reading_another_users_round_is_404()
    {
        var owner = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var other = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(owner.AccessToken);
        var interview = await CreateInterviewAsync(owner.AccessToken, appId);

        var response = await _client.GetInterviewAsync(other.AccessToken, appId, interview.Id);

        await response.ShouldBeProblemAsync(404, "interview.not_found");
    }

    [Fact]
    public async Task A_round_read_under_the_wrong_application_is_404()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);
        var otherApp = await SeedApplicationAsync(tokens.AccessToken);
        var interview = await CreateInterviewAsync(tokens.AccessToken, appId);

        // Right interview id, wrong parent in the path.
        var response = await _client.GetInterviewAsync(tokens.AccessToken, otherApp, interview.Id);

        await response.ShouldBeProblemAsync(404, "interview.not_found");
    }

    [Fact]
    public async Task Listing_under_another_users_application_is_404()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken);

        var response = await _client.ListInterviewsAsync(mine.AccessToken, theirApp);

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.ListInterviewsAsync(accessToken: null, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedApplicationAsync(string? accessToken) =>
        (await (await _client.CreateApplicationAsync(accessToken, new { role = "Engineer" })).ReadApplicationAsync()).Id;

    private async Task<InterviewView> CreateInterviewAsync(
        string? accessToken, Guid applicationId, string scheduledAt = "2026-08-01T14:00:00Z", string? notes = null) =>
        await (await _client.CreateInterviewAsync(
            accessToken, applicationId, new { scheduledAt, type = "technical", format = "remote", notes })).ReadInterviewAsync();
}
