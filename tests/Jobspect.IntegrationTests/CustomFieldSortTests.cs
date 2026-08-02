using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Ordering the application list by a custom-field answer, against real
/// PostgreSQL. Two things carry the weight here: each type has to order the way a
/// person would expect - numbers as numbers, dates chronologically - and
/// applications that never answered the field have to sort last whichever way the
/// answers run, and keep doing so across a page boundary.
/// <para>
/// That last point is why the resume condition has three cases instead of two, and
/// the paging tests below are the only thing that would notice if it were wrong:
/// a two-case predicate returns perfectly plausible pages that quietly repeat or
/// drop the rows around the boundary.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomFieldSortTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Text_answers_order_alphabetically_both_ways()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");

        var b = await CreateAsync(tokens, "B", (field.Id, "Beta"));
        var a = await CreateAsync(tokens, "A", (field.Id, "Alpha"));
        var c = await CreateAsync(tokens, "C", (field.Id, "Gamma"));

        (await SortedAsync(tokens, field.Id, "asc")).ShouldBe([a.Id, b.Id, c.Id]);
        (await SortedAsync(tokens, field.Id, "desc")).ShouldBe([c.Id, b.Id, a.Id]);
    }

    [Fact]
    public async Task Numbers_order_as_numbers_not_as_text()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Priority", "number");

        var nine = await CreateAsync(tokens, "Nine", (field.Id, 9));
        var ten = await CreateAsync(tokens, "Ten", (field.Id, 10));
        var two = await CreateAsync(tokens, "Two", (field.Id, 2));

        // Sorted as text this would be 10, 2, 9 - which is the bug the numeric
        // cast exists to avoid, and the reason number is the one type that gets one.
        (await SortedAsync(tokens, field.Id, "asc")).ShouldBe([two.Id, nine.Id, ten.Id]);
    }

    [Fact]
    public async Task Dates_order_chronologically()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Follow up on", "date");

        var later = await CreateAsync(tokens, "Later", (field.Id, "2026-10-02"));
        var soon = await CreateAsync(tokens, "Soon", (field.Id, "2026-09-30"));

        // No cast needed: ISO-8601 sorts lexically exactly as it sorts in time,
        // which is the whole reason the format was chosen for storage.
        (await SortedAsync(tokens, field.Id, "asc")).ShouldBe([soon.Id, later.Id]);
    }

    [Fact]
    public async Task Checkboxes_order_false_before_true()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Remote ok", "checkbox");

        var yes = await CreateAsync(tokens, "Remote", (field.Id, true));
        var no = await CreateAsync(tokens, "Onsite", (field.Id, false));

        (await SortedAsync(tokens, field.Id, "asc")).ShouldBe([no.Id, yes.Id]);
        (await SortedAsync(tokens, field.Id, "desc")).ShouldBe([yes.Id, no.Id]);
    }

    [Fact]
    public async Task Unanswered_applications_sort_last_whichever_way_the_answers_run()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");

        var alpha = await CreateAsync(tokens, "Alpha", (field.Id, "Alpha"));
        var zulu = await CreateAsync(tokens, "Zulu", (field.Id, "Zulu"));
        var silent = await CreateAsync(tokens, "Silent");

        // NULLS LAST in both directions, so the tail is a tail rather than a head
        // that appears when the client flips the arrow.
        (await SortedAsync(tokens, field.Id, "asc")).ShouldBe([alpha.Id, zulu.Id, silent.Id]);
        (await SortedAsync(tokens, field.Id, "desc")).ShouldBe([zulu.Id, alpha.Id, silent.Id]);
    }

    [Fact]
    public async Task A_sorted_list_pages_across_the_boundary_into_the_unanswered_tail()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");

        var alpha = await CreateAsync(tokens, "Alpha", (field.Id, "Alpha"));
        var bravo = await CreateAsync(tokens, "Bravo", (field.Id, "Bravo"));
        var quiet = await CreateAsync(tokens, "Quiet");
        var silent = await CreateAsync(tokens, "Silent");

        var expected = new[] { alpha.Id, bravo.Id, quiet.Id, silent.Id };

        // One row at a time, so every page boundary is exercised - including the
        // one from the last answer into the unanswered tail, and the one from an
        // unanswered row to the next unanswered row. Those are the two branches a
        // simpler predicate gets wrong.
        var walked = await WalkAsync(tokens, field.Id, "asc", limit: 1);

        walked.ShouldBe(expected);
    }

    [Fact]
    public async Task Paging_a_descending_sort_walks_the_same_rows_once()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Priority", "number");

        var ids = new List<Guid>();
        for (var value = 1; value <= 5; value++)
        {
            ids.Add((await CreateAsync(tokens, $"P{value}", (field.Id, value))).Id);
        }

        var unanswered = await CreateAsync(tokens, "Unanswered");
        ids.Reverse();
        ids.Add(unanswered.Id);

        (await WalkAsync(tokens, field.Id, "desc", limit: 2)).ShouldBe(ids);
    }

    [Fact]
    public async Task Rows_that_share_an_answer_are_still_walked_once()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Team", "singleSelect", ["Platform"]);

        var ids = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            ids.Add((await CreateAsync(tokens, $"Same {index}", (field.Id, "Platform"))).Id);
        }

        // Every row has the identical answer, so the id tiebreak is the only thing
        // separating them - and ascending means ascending ids.
        var walked = await WalkAsync(tokens, field.Id, "asc", limit: 1);

        walked.Count.ShouldBe(4);
        walked.ShouldBe([.. ids.OrderBy(id => id)]);
    }

    [Fact]
    public async Task A_sort_combines_with_a_filter()
    {
        var tokens = await ProUserAsync();
        var team = await DefineAsync(tokens, "Team", "singleSelect", ["Platform", "Product"]);
        var priority = await DefineAsync(tokens, "Priority", "number");

        var high = await CreateAsync(tokens, "High", (team.Id, "Platform"), (priority.Id, 9));
        var low = await CreateAsync(tokens, "Low", (team.Id, "Platform"), (priority.Id, 1));
        await CreateAsync(tokens, "Other team", (team.Id, "Product"), (priority.Id, 5));

        var rows = await (await _client.ListApplicationsAsync(
            tokens.AccessToken,
            customFieldId: team.Id,
            customFieldValue: "Platform",
            sortCustomFieldId: priority.Id,
            sortDirection: "desc")).ReadApplicationListAsync();

        rows.Select(row => row.Id).ShouldBe([high.Id, low.Id]);
    }

    [Fact]
    public async Task A_sort_never_reaches_another_users_applications()
    {
        var mine = await ProUserAsync();
        var field = await DefineAsync(mine, "Referral source", "text");
        await CreateAsync(mine, "Mine", (field.Id, "LinkedIn"));

        var theirs = await ProUserAsync();
        var theirField = await DefineAsync(theirs, "Referral source", "text");

        (await SortedAsync(theirs, theirField.Id, "asc")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_multi_select_cannot_be_sorted_by()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Stack", "multiSelect", ["Go", "Rust"]);

        // A set has no order that isn't invented.
        await (await _client.ListApplicationsAsync(tokens.AccessToken, sortCustomFieldId: field.Id))
            .ShouldBeProblemAsync(422, "custom_field.not_sortable");
    }

    [Fact]
    public async Task Sorting_by_a_field_the_caller_does_not_have_is_refused()
    {
        var tokens = await ProUserAsync();

        await (await _client.ListApplicationsAsync(tokens.AccessToken, sortCustomFieldId: Guid.CreateVersion7()))
            .ShouldBeProblemAsync(422, "custom_field.unknown_field");
    }

    [Fact]
    public async Task A_direction_without_a_field_is_refused()
    {
        var tokens = await ProUserAsync();

        await (await _client.ListApplicationsAsync(tokens.AccessToken, sortDirection: "asc"))
            .ShouldBeValidationProblemAsync("sortCustomFieldId");

        var field = await DefineAsync(tokens, "Referral source", "text");
        await (await _client.ListApplicationsAsync(
                tokens.AccessToken, sortCustomFieldId: field.Id, sortDirection: "sideways"))
            .ShouldBeValidationProblemAsync("sortDirection");
    }

    [Fact]
    public async Task A_cursor_does_not_cross_between_the_two_orders()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        await CreateAsync(tokens, "One", (field.Id, "Alpha"));
        await CreateAsync(tokens, "Two", (field.Id, "Bravo"));

        var sorted = await (await _client.ListApplicationsAsync(
            tokens.AccessToken, limit: 1, sortCustomFieldId: field.Id, sortDirection: "asc"))
            .ReadPageAsync<ApplicationSummaryView>();
        sorted.NextCursor.ShouldNotBeNull();

        // The two orders position rows differently, so a cursor from one means
        // nothing to the other. Refused rather than silently restarting, which
        // would let a client page the same rows forever.
        await (await _client.ListApplicationsAsync(tokens.AccessToken, cursor: sorted.NextCursor))
            .ShouldBeValidationProblemAsync("cursor");

        var byDate = await (await _client.ListApplicationsAsync(tokens.AccessToken, limit: 1))
            .ReadPageAsync<ApplicationSummaryView>();
        byDate.NextCursor.ShouldNotBeNull();

        await (await _client.ListApplicationsAsync(
                tokens.AccessToken, cursor: byDate.NextCursor, sortCustomFieldId: field.Id))
            .ShouldBeValidationProblemAsync("cursor");
    }

    [Fact]
    public async Task A_cursor_from_a_sort_on_another_field_is_refused()
    {
        var tokens = await ProUserAsync();
        var text = await DefineAsync(tokens, "Referral source", "text");
        var number = await DefineAsync(tokens, "Priority", "number");
        await CreateAsync(tokens, "One", (text.Id, "Alpha"), (number.Id, 1));
        await CreateAsync(tokens, "Two", (text.Id, "Bravo"), (number.Id, 2));

        var page = await (await _client.ListApplicationsAsync(
            tokens.AccessToken, limit: 1, sortCustomFieldId: text.Id, sortDirection: "asc"))
            .ReadPageAsync<ApplicationSummaryView>();

        // Both are answer-shaped cursors, so the paging check lets it through - the
        // handler is what notices "Alpha" is not a position in a numeric order.
        await (await _client.ListApplicationsAsync(
                tokens.AccessToken, cursor: page.NextCursor, sortCustomFieldId: number.Id, sortDirection: "asc"))
            .ShouldBeProblemAsync(422, "cursor.sort_mismatch");
    }

    [Fact]
    public async Task Sorting_by_a_custom_field_needs_the_entitlement()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        await CreateAsync(tokens, "One", (field.Id, "Alpha"));

        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        (await _client.ListApplicationsAsync(tokens.AccessToken, sortCustomFieldId: field.Id))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The default order is untouched - ordering by a custom field is the
        // capability, not reading the list.
        (await _client.ListApplicationsAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Walks every page at the given size and returns the rows in the order they arrived.</summary>
    private async Task<IReadOnlyList<Guid>> WalkAsync(AuthTokens tokens, Guid fieldId, string direction, int limit)
    {
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await (await _client.ListApplicationsAsync(
                tokens.AccessToken,
                limit: limit,
                cursor: cursor,
                sortCustomFieldId: fieldId,
                sortDirection: direction)).ReadPageAsync<ApplicationSummaryView>();

            seen.AddRange(page.Items.Select(row => row.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return seen;
    }

    private async Task<IReadOnlyList<Guid>> SortedAsync(AuthTokens tokens, Guid fieldId, string direction) =>
        [.. (await (await _client.ListApplicationsAsync(
            tokens.AccessToken, sortCustomFieldId: fieldId, sortDirection: direction))
            .ReadApplicationListAsync()).Select(row => row.Id)];

    private async Task<AuthTokens> ProUserAsync()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        await fixture.UnlockProAsync(_client, tokens, Ct);

        return tokens;
    }

    private async Task<CustomFieldView> DefineAsync(
        AuthTokens tokens, string label, string type, string[]? options = null) =>
        await (await _client.CreateCustomFieldAsync(tokens.AccessToken, new { label, type, options }))
            .ReadCustomFieldAsync();

    private async Task<ApplicationView> CreateAsync(
        AuthTokens tokens, string role, params (Guid Field, object? Value)[] answers) =>
        await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role,
            customFields = answers.ToDictionary(answer => answer.Field.ToString(), answer => answer.Value),
        })).ReadApplicationAsync();
}
