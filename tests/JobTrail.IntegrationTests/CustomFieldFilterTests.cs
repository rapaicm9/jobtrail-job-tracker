using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Narrowing the application list by a custom-field answer, against real
/// PostgreSQL. The filter is a JSONB containment test, so the value a client sends
/// as text has to become the JSON its field actually stores before it can match -
/// a probe of <c>"3"</c> against a stored <c>3</c> contains nothing, and the page
/// would come back confidently empty. Most of what is asserted here is that
/// coercion being right.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomFieldFilterTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Filtering_on_text_returns_only_the_applications_that_answered_it()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");

        var matching = await CreateAsync(tokens, "Matching", (field.Id, "LinkedIn"));
        await CreateAsync(tokens, "Different", (field.Id, "Referral"));
        await CreateAsync(tokens, "Unanswered");

        var rows = await ListAsync(tokens, field.Id, "LinkedIn");

        // The one that answered, and neither the one that answered differently nor
        // the one that never answered at all.
        rows.Select(row => row.Id).ShouldBe([matching.Id]);
    }

    [Fact]
    public async Task A_number_filter_matches_the_number_and_not_its_text()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Priority", "number");
        var created = await CreateAsync(tokens, "Priority three", (field.Id, 3));

        // The client sends "3" either way; only a probe coerced to a JSON number
        // contains a stored JSON number.
        (await ListAsync(tokens, field.Id, "3")).Select(row => row.Id).ShouldBe([created.Id]);

        // And a different number does not match.
        (await ListAsync(tokens, field.Id, "4")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_checkbox_filter_matches_the_boolean()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Remote ok", "checkbox");
        var yes = await CreateAsync(tokens, "Remote", (field.Id, true));
        await CreateAsync(tokens, "Onsite", (field.Id, false));

        (await ListAsync(tokens, field.Id, "true")).Select(row => row.Id).ShouldBe([yes.Id]);
    }

    [Fact]
    public async Task A_date_filter_matches_the_stored_day()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Follow up on", "date");
        var created = await CreateAsync(tokens, "Due", (field.Id, "2026-09-01"));
        await CreateAsync(tokens, "Later", (field.Id, "2026-10-01"));

        (await ListAsync(tokens, field.Id, "2026-09-01")).Select(row => row.Id).ShouldBe([created.Id]);
    }

    [Fact]
    public async Task A_multi_select_filter_asks_whether_the_option_is_among_them()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Stack", "multiSelect", ["Go", "Rust", "C#"]);
        var polyglot = await CreateAsync(tokens, "Go and Rust", (field.Id, new[] { "Go", "Rust" }));
        await CreateAsync(tokens, "C# only", (field.Id, new[] { "C#" }));

        // Containment on an array is membership, which is what filtering a
        // multi-select has to mean - not "answered with exactly this set".
        (await ListAsync(tokens, field.Id, "Rust")).Select(row => row.Id).ShouldBe([polyglot.Id]);
    }

    [Fact]
    public async Task A_single_select_filter_matches_the_chosen_option()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Team", "singleSelect", ["Platform", "Product"]);
        var platform = await CreateAsync(tokens, "Platform role", (field.Id, "Platform"));
        await CreateAsync(tokens, "Product role", (field.Id, "Product"));

        (await ListAsync(tokens, field.Id, "Platform")).Select(row => row.Id).ShouldBe([platform.Id]);
    }

    [Fact]
    public async Task A_filter_never_reaches_another_users_applications()
    {
        var mine = await ProUserAsync();
        var field = await DefineAsync(mine, "Referral source", "text");
        await CreateAsync(mine, "Mine", (field.Id, "LinkedIn"));

        var theirs = await ProUserAsync();

        // Their own field, their own id - and the owner filter still applies, so
        // the answer is that they have nothing rather than a peek at mine.
        var theirField = await DefineAsync(theirs, "Referral source", "text");
        (await ListAsync(theirs, theirField.Id, "LinkedIn")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_archived_field_still_filters()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Old process", "text");
        var created = await CreateAsync(tokens, "Recorded while live", (field.Id, "LinkedIn"));

        (await _client.UpdateCustomFieldAsync(
            tokens.AccessToken, field.Id, new { label = field.Label, isArchived = true }))
            .IsSuccessStatusCode.ShouldBeTrue();

        // The answers are still there and still read back, so refusing to search
        // them would be a strange kind of tidiness.
        (await ListAsync(tokens, field.Id, "LinkedIn")).Select(row => row.Id).ShouldBe([created.Id]);
    }

    [Fact]
    public async Task A_filtered_list_pages_like_any_other()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");

        for (var index = 0; index < 3; index++)
        {
            await CreateAsync(tokens, $"Match {index}", (field.Id, "LinkedIn"));
        }

        await CreateAsync(tokens, "Not a match", (field.Id, "Referral"));

        var first = await (await _client.ListApplicationsAsync(
            tokens.AccessToken, limit: 2, customFieldId: field.Id, customFieldValue: "LinkedIn"))
            .ReadPageAsync<ApplicationSummaryView>();

        first.Items.Count.ShouldBe(2);
        first.NextCursor.ShouldNotBeNull();

        var second = await (await _client.ListApplicationsAsync(
            tokens.AccessToken, cursor: first.NextCursor, customFieldId: field.Id, customFieldValue: "LinkedIn"))
            .ReadPageAsync<ApplicationSummaryView>();

        // The filter is resent with the cursor - a cursor is a position, not a
        // saved query - and the page after it holds the last match and no more.
        second.Items.Count.ShouldBe(1);
        second.NextCursor.ShouldBeNull();
        second.Items.Select(row => row.Id).ShouldNotBeOneOf([.. first.Items.Select(row => row.Id)]);
    }

    [Fact]
    public async Task A_filter_on_a_field_the_caller_does_not_have_is_refused()
    {
        var tokens = await ProUserAsync();

        await (await _client.ListApplicationsAsync(
                tokens.AccessToken, customFieldId: Guid.CreateVersion7(), customFieldValue: "x"))
            .ShouldBeProblemAsync(422, "custom_field.unknown_field");
    }

    [Theory]
    [InlineData("number", "twelve")]
    [InlineData("checkbox", "yes")]
    [InlineData("date", "01/02/2026")]
    public async Task A_filter_value_that_cannot_be_the_fields_type_is_refused(string type, string value)
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, $"Field {type}", type);

        // Refused rather than silently matching nothing: an empty page is what a
        // client sees when the answer really is "none", and the two must differ.
        await (await _client.ListApplicationsAsync(
                tokens.AccessToken, customFieldId: field.Id, customFieldValue: value))
            .ShouldBeProblemAsync(422, "custom_field.value_invalid");
    }

    [Fact]
    public async Task Half_a_filter_is_refused()
    {
        var tokens = await ProUserAsync();

        // A list that returns everything when it was asked to narrow is a bug
        // found late; say so now.
        await (await _client.ListApplicationsAsync(tokens.AccessToken, customFieldId: Guid.CreateVersion7()))
            .ShouldBeValidationProblemAsync("customFieldValue");

        await (await _client.ListApplicationsAsync(tokens.AccessToken, customFieldValue: "LinkedIn"))
            .ShouldBeValidationProblemAsync("customFieldId");
    }

    [Fact]
    public async Task Filtering_by_a_custom_field_needs_the_entitlement()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        await CreateAsync(tokens, "Matching", (field.Id, "LinkedIn"));

        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        (await _client.ListApplicationsAsync(
                tokens.AccessToken, customFieldId: field.Id, customFieldValue: "LinkedIn"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The plain list is untouched - searching is the capability, not reading.
        (await _client.ListApplicationsAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_containment_index_exists()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();

        var definition = await db.Database.SqlQueryRaw<string>(
            """
            SELECT indexdef AS "Value" FROM pg_indexes
            WHERE schemaname = 'applications'
              AND tablename = 'applications'
              AND indexname = 'ix_applications_custom_field_values'
            """).SingleOrDefaultAsync(Ct);

        // Asserted by definition rather than by watching the planner choose it: at
        // test-data volumes a sequential scan is the cheaper plan and Postgres is
        // right to take it, so a plan assertion would fail for the wrong reason.
        // What is worth pinning is the operator class - the default one indexes far
        // more than the single operator this filter uses.
        definition.ShouldNotBeNull();
        definition.ShouldContain("USING gin");
        definition.ShouldContain("jsonb_path_ops");
    }

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

    private async Task<IReadOnlyList<ApplicationSummaryView>> ListAsync(
        AuthTokens tokens, Guid fieldId, string value) =>
        await (await _client.ListApplicationsAsync(
            tokens.AccessToken, customFieldId: fieldId, customFieldValue: value)).ReadApplicationListAsync();
}
