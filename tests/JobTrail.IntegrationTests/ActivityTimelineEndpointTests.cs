using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The activity slices under <c>/api/v1/applications/{id}/activity</c> against a
/// real database: notes join the automatic entries on one newest-first feed, the
/// entries carry what their kind implies, and ownership flows through the parent
/// application - someone else's timeline is a 404.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ActivityTimelineEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Adds_a_note_and_returns_the_entry()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.AddNoteAsync(tokens.AccessToken, appId, new
        {
            note = "  Recruiter called - they want a second round.  ",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var entry = await response.ReadActivityEntryAsync();
        entry.Kind.ShouldBe("Note");
        entry.Note.ShouldBe("Recruiter called - they want a second round.");
        entry.FromStage.ShouldBeNull();
        entry.ToStage.ShouldBeNull();
        entry.TransitionKind.ShouldBeNull();

        // The id and time come back from the database, and the entry the write
        // returned is the one the timeline then reads.
        entry.Id.ShouldNotBe(Guid.Empty);
        var timeline = await ReadTimelineAsync(tokens.AccessToken, appId);
        timeline.ShouldContain(entry);
    }

    [Fact]
    public async Task Timeline_opens_with_the_creation_entry()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var timeline = await ReadTimelineAsync(tokens.AccessToken, appId);

        var created = timeline.ShouldHaveSingleItem();
        created.Kind.ShouldBe("Created");
        created.ToStage.ShouldBe("Applied");
        created.FromStage.ShouldBeNull();
        created.Note.ShouldBeNull();
    }

    [Fact]
    public async Task Interleaves_notes_and_stage_changes_newest_first()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        await AddNoteAsync(tokens.AccessToken, appId, "Sent a follow-up.");
        (await _client.TransitionApplicationAsync(tokens.AccessToken, appId, "Screening"))
            .IsSuccessStatusCode.ShouldBeTrue();
        await AddNoteAsync(tokens.AccessToken, appId, "Screening call booked.");

        var timeline = await ReadTimelineAsync(tokens.AccessToken, appId);

        timeline.Select(e => e.Kind).ShouldBe(["Note", "StageChanged", "Note", "Created"]);
        timeline[0].Note.ShouldBe("Screening call booked.");
        timeline[1].FromStage.ShouldBe("Applied");
        timeline[1].ToStage.ShouldBe("Screening");
        timeline[1].TransitionKind.ShouldBe("Advance");
        timeline[2].Note.ShouldBe("Sent a follow-up.");
        timeline.Select(e => e.OccurredAt).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task Note_text_is_required()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.AddNoteAsync(tokens.AccessToken, appId, new { note = "   " });

        await response.ShouldBeValidationProblemAsync("note");
    }

    [Fact]
    public async Task Rejects_a_note_past_the_length_cap()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.AddNoteAsync(tokens.AccessToken, appId, new { note = new string('x', 2001) });

        await response.ShouldBeValidationProblemAsync("note");
    }

    [Fact]
    public async Task Noting_another_users_application_is_404()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken);

        var response = await _client.AddNoteAsync(mine.AccessToken, theirApp, new { note = "Prying." });

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Reading_another_users_timeline_is_404()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken);
        await AddNoteAsync(theirs.AccessToken, theirApp, "Private.");

        var response = await _client.GetActivityAsync(mine.AccessToken, theirApp);

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.GetActivityAsync(accessToken: null, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedApplicationAsync(string? accessToken) =>
        (await (await _client.CreateApplicationAsync(accessToken, new { role = "Engineer" })).ReadApplicationAsync()).Id;

    private async Task<ActivityEntryView> AddNoteAsync(string? accessToken, Guid applicationId, string note) =>
        await (await _client.AddNoteAsync(accessToken, applicationId, new { note })).ReadActivityEntryAsync();

    private async Task<IReadOnlyList<ActivityEntryView>> ReadTimelineAsync(string? accessToken, Guid applicationId) =>
        await (await _client.GetActivityAsync(accessToken, applicationId)).ReadActivityAsync();
}
