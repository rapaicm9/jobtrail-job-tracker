using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Cursor paging across the list endpoints, against a real database. The claim
/// under test is that walking a list page by page yields exactly the same rows, in
/// the same order, as reading it in one go - including where the sort key repeats,
/// which is the edge a naive cursor drops or duplicates rows at. Ownership and the
/// parent-application check still hold with a cursor in hand.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListPaginationTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Returns_the_whole_list_and_no_cursor_when_it_fits_on_one_page()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        await SeedApplicationAsync(tokens.AccessToken, "2026-07-09");

        var page = await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadPageAsync<ApplicationSummaryView>();

        page.Items.Count.ShouldBe(2);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Walks_a_list_page_by_page_without_dropping_or_repeating_a_row()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        foreach (var day in new[] { "2026-07-10", "2026-07-09", "2026-07-08", "2026-07-07", "2026-07-06" })
        {
            await SeedApplicationAsync(tokens.AccessToken, day);
        }

        var unpaged = (await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync())
            .Select(a => a.Id)
            .ToArray();
        var walked = await WalkApplicationsAsync(tokens.AccessToken, limit: 2);

        walked.ShouldBe(unpaged);
    }

    [Fact]
    public async Task Does_not_end_on_an_empty_page_when_the_row_count_divides_evenly()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        await SeedApplicationAsync(tokens.AccessToken, "2026-07-09");

        var first = await (await _client.ListApplicationsAsync(tokens.AccessToken, limit: 2))
            .ReadPageAsync<ApplicationSummaryView>();

        // Two rows, two per page: the page is full, but nothing follows it - so the
        // client is told to stop rather than asking for an empty page.
        first.Items.Count.ShouldBe(2);
        first.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Breaks_ties_on_the_sort_key_across_a_page_edge()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // All applied the same day: the date alone cannot order them, so the page
        // edge rests entirely on the id tiebreak.
        for (var i = 0; i < 3; i++)
        {
            await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        }

        var unpaged = (await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync())
            .Select(a => a.Id)
            .ToArray();
        var walked = await WalkApplicationsAsync(tokens.AccessToken, limit: 1);

        walked.ShouldBe(unpaged);
        walked.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Pages_contacts_by_name_including_repeated_names()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        foreach (var name in new[] { "Bo Lee", "Alex Kim", "Alex Kim" })
        {
            await CreateContactAsync(tokens.AccessToken, appId, name);
        }

        var unpaged = (await (await _client.ListContactsAsync(tokens.AccessToken)).ReadContactListAsync())
            .Select(c => c.Id)
            .ToArray();

        var walked = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await (await _client.ListContactsAsync(tokens.AccessToken, limit: 1, cursor: cursor))
                .ReadPageAsync<ContactView>();
            walked.AddRange(page.Items.Select(c => c.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null && walked.Count < 10);

        walked.ShouldBe(unpaged);
    }

    [Fact]
    public async Task Pages_contacts_alongside_a_filter()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var mine = await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        var other = await SeedApplicationAsync(tokens.AccessToken, "2026-07-09");
        await CreateContactAsync(tokens.AccessToken, mine, "Alex Kim");
        await CreateContactAsync(tokens.AccessToken, mine, "Bo Lee");
        await CreateContactAsync(tokens.AccessToken, other, "Chris Vale");

        var first = await (await _client.ListContactsAsync(tokens.AccessToken, applicationId: mine, limit: 1))
            .ReadPageAsync<ContactView>();
        first.Items.Single().Name.ShouldBe("Alex Kim");

        var second = await (await _client.ListContactsAsync(
            tokens.AccessToken, applicationId: mine, limit: 1, cursor: first.NextCursor)).ReadPageAsync<ContactView>();

        // The filter travels with the cursor, so the other application's contact
        // never appears and the page after the last one is the end.
        second.Items.Single().Name.ShouldBe("Bo Lee");
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Pages_the_activity_timeline_newest_first()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        await AddNoteAsync(tokens.AccessToken, appId, "First note.");
        await AddNoteAsync(tokens.AccessToken, appId, "Second note.");

        var unpaged = (await (await _client.GetActivityAsync(tokens.AccessToken, appId)).ReadActivityAsync())
            .Select(e => e.Id)
            .ToArray();
        unpaged.Length.ShouldBe(3); // the creation entry plus both notes

        var walked = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await (await _client.GetActivityAsync(tokens.AccessToken, appId, limit: 2, cursor: cursor))
                .ReadPageAsync<ActivityEntryView>();
            walked.AddRange(page.Items.Select(e => e.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null && walked.Count < 10);

        walked.ShouldBe(unpaged);
    }

    [Fact]
    public async Task Pages_interview_rounds()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        foreach (var scheduledAt in new[] { "2026-08-20T10:00:00Z", "2026-08-01T10:00:00Z", "2026-08-10T10:00:00Z" })
        {
            await CreateInterviewAsync(tokens.AccessToken, appId, scheduledAt);
        }

        var first = await (await _client.ListInterviewsAsync(tokens.AccessToken, appId, limit: 2))
            .ReadPageAsync<InterviewView>();
        var second = await (await _client.ListInterviewsAsync(tokens.AccessToken, appId, limit: 2, cursor: first.NextCursor))
            .ReadPageAsync<InterviewView>();

        first.Items.Select(i => i.ScheduledAt.UtcDateTime.Day).ShouldBe([1, 10]);
        second.Items.Select(i => i.ScheduledAt.UtcDateTime.Day).ShouldBe([20]);
        second.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Rejects_a_limit_outside_the_allowed_range(int limit)
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.ListApplicationsAsync(tokens.AccessToken, limit);

        await response.ShouldBeValidationProblemAsync("limit");
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("!!!!")]
    [InlineData("YWJj")]
    public async Task Rejects_a_cursor_that_is_not_one(string cursor)
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.ListApplicationsAsync(tokens.AccessToken, cursor: cursor);

        await response.ShouldBeValidationProblemAsync("cursor");
    }

    [Fact]
    public async Task Rejects_a_cursor_issued_by_a_different_list()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken, "2026-07-10");
        await CreateContactAsync(tokens.AccessToken, appId, "Alex Kim");
        await CreateContactAsync(tokens.AccessToken, appId, "Bo Lee");

        // A contacts cursor carries a name where the applications list expects a date.
        var contacts = await (await _client.ListContactsAsync(tokens.AccessToken, limit: 1))
            .ReadPageAsync<ContactView>();
        contacts.NextCursor.ShouldNotBeNull();

        var response = await _client.ListApplicationsAsync(tokens.AccessToken, cursor: contacts.NextCursor);

        await response.ShouldBeValidationProblemAsync("cursor");
    }

    [Fact]
    public async Task A_cursor_never_reaches_another_users_rows()
    {
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        await SeedApplicationAsync(theirs.AccessToken, "2026-07-10");
        await SeedApplicationAsync(theirs.AccessToken, "2026-07-09");
        var theirPage = await (await _client.ListApplicationsAsync(theirs.AccessToken, limit: 1))
            .ReadPageAsync<ApplicationSummaryView>();
        theirPage.NextCursor.ShouldNotBeNull();

        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var myApplication = await SeedApplicationAsync(mine.AccessToken, "2026-07-01");

        // Their cursor only says "start after this date and id" - the owner filter
        // is the query's, so it positions inside my own list and nothing else.
        var page = await (await _client.ListApplicationsAsync(mine.AccessToken, cursor: theirPage.NextCursor))
            .ReadPageAsync<ApplicationSummaryView>();

        page.Items.Select(a => a.Id).ShouldBe([myApplication]);
    }

    [Fact]
    public async Task Another_users_application_is_still_404_with_a_valid_cursor()
    {
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken, "2026-07-10");
        await AddNoteAsync(theirs.AccessToken, theirApp, "First note.");
        await AddNoteAsync(theirs.AccessToken, theirApp, "Second note.");
        var theirPage = await (await _client.GetActivityAsync(theirs.AccessToken, theirApp, limit: 1))
            .ReadPageAsync<ActivityEntryView>();
        theirPage.NextCursor.ShouldNotBeNull();

        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.GetActivityAsync(mine.AccessToken, theirApp, cursor: theirPage.NextCursor);

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    private async Task<IReadOnlyList<Guid>> WalkApplicationsAsync(string? accessToken, int limit)
    {
        var walked = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await (await _client.ListApplicationsAsync(accessToken, limit, cursor))
                .ReadPageAsync<ApplicationSummaryView>();
            page.Items.Count.ShouldBeLessThanOrEqualTo(limit);
            walked.AddRange(page.Items.Select(a => a.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null && walked.Count < 50);

        return walked;
    }

    private async Task<Guid> SeedApplicationAsync(string? accessToken, string appliedDate) =>
        (await (await _client.CreateApplicationAsync(accessToken, new { role = "Engineer", appliedDate }))
            .ReadApplicationAsync()).Id;

    private Task<HttpResponseMessage> CreateContactAsync(string? accessToken, Guid applicationId, string name) =>
        _client.CreateContactAsync(accessToken, new { applicationId, name });

    private Task<HttpResponseMessage> CreateInterviewAsync(string? accessToken, Guid applicationId, string scheduledAt) =>
        _client.CreateInterviewAsync(
            accessToken, applicationId, new { scheduledAt, type = "technical", format = "remote" });

    private Task<HttpResponseMessage> AddNoteAsync(string? accessToken, Guid applicationId, string note) =>
        _client.AddNoteAsync(accessToken, applicationId, new { note });
}
