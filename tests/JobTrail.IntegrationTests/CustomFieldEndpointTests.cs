using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The custom-field definition surface against a real database: a Pro account
/// defines fields, reads them back whole, edits and retires them, and is held to
/// the rules that keep the definitions usable - one live field per name, options
/// only where they mean something, and a bounded number of them.
/// <para>
/// Every test here works from a Pro account, because defining a field is the paid
/// capability. <see cref="CustomFieldGateTests"/> is where the gate itself is the
/// subject.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomFieldEndpointTests(ApiFixture fixture)
{
    /// <summary>
    /// The module's cap, spelled out here rather than read from it: a test that
    /// takes the limit from the code it is checking proves only that the code
    /// agrees with itself, and this number is part of the contract.
    /// </summary>
    private const int FieldLimit = 50;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Defining_a_field_returns_it_with_a_location()
    {
        var tokens = await ProUserAsync();

        var response = await _client.CreateCustomFieldAsync(
            tokens.AccessToken, new { label = "Referral source", type = "text" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadCustomFieldAsync();

        created.Label.ShouldBe("Referral source");
        created.Type.ShouldBe("Text");
        created.Options.ShouldBeEmpty();
        created.IsArchived.ShouldBeFalse();
        created.UpdatedAt.ShouldBeNull();

        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/custom-fields/{created.Id}");
    }

    [Fact]
    public async Task A_select_keeps_its_options_in_the_order_they_were_given()
    {
        var tokens = await ProUserAsync();

        var created = await (await _client.CreateCustomFieldAsync(tokens.AccessToken, new
        {
            label = "Team",
            type = "singleSelect",
            options = new[] { "Platform", "Product", "Data" },
        })).ReadCustomFieldAsync();

        created.Type.ShouldBe("SingleSelect");

        // Order is the user's choice - it is the order the options appear in a form.
        created.Options.ShouldBe(["Platform", "Product", "Data"]);
    }

    [Fact]
    public async Task A_field_reads_back_and_lists_whole()
    {
        var tokens = await ProUserAsync();

        var first = await CreateAsync(tokens.AccessToken, "Referral source", "text");
        var second = await CreateAsync(tokens.AccessToken, "Priority", "number");

        (await (await _client.GetCustomFieldAsync(tokens.AccessToken, first.Id)).ReadCustomFieldAsync())
            .Label.ShouldBe("Referral source");

        // A bare array, not the paged envelope, and in the order they were defined.
        var listed = await (await _client.ListCustomFieldsAsync(tokens.AccessToken)).ReadCustomFieldListAsync();
        listed.Select(definition => definition.Id).ShouldBe([first.Id, second.Id]);
    }

    [Fact]
    public async Task Updating_replaces_the_editable_parts()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Team", "multiSelect", ["Platform", "Product"]);

        var updated = await (await _client.UpdateCustomFieldAsync(tokens.AccessToken, created.Id, new
        {
            label = "Team or squad",
            options = new[] { "Platform", "Product", "Data" },
            isArchived = false,
        })).ReadCustomFieldAsync();

        updated.Label.ShouldBe("Team or squad");
        updated.Options.ShouldBe(["Platform", "Product", "Data"]);
        updated.UpdatedAt.ShouldNotBeNull();

        // The type is not in the payload at all: it is fixed at creation because
        // the values already recorded were given under it.
        updated.Type.ShouldBe("MultiSelect");
    }

    [Fact]
    public async Task Archiving_retires_a_field_without_removing_it()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Old process", "text");

        await ArchiveAsync(tokens.AccessToken, created);

        // Still listed, flagged rather than hidden - a client rendering an existing
        // application has to label the values recorded against it.
        var listed = await (await _client.ListCustomFieldsAsync(tokens.AccessToken)).ReadCustomFieldListAsync();
        listed.ShouldHaveSingleItem().IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task A_second_live_field_may_not_take_a_name_already_in_use()
    {
        var tokens = await ProUserAsync();
        await CreateAsync(tokens.AccessToken, "Referral source", "text");

        // Compared the way a person reads it: case is not what makes a name different.
        var clash = await _client.CreateCustomFieldAsync(
            tokens.AccessToken, new { label = "referral SOURCE", type = "text" });

        await clash.ShouldBeProblemAsync(409, "custom_field.label_taken");
    }

    [Fact]
    public async Task Archiving_a_field_frees_its_name_again()
    {
        var tokens = await ProUserAsync();
        var original = await CreateAsync(tokens.AccessToken, "Referral source", "text");

        await ArchiveAsync(tokens.AccessToken, original);

        // The uniqueness index only constrains live fields, so the name comes back.
        var replacement = await CreateAsync(tokens.AccessToken, "Referral source", "text");
        replacement.Id.ShouldNotBe(original.Id);
    }

    [Fact]
    public async Task Bringing_a_field_back_onto_a_name_since_reused_is_refused()
    {
        var tokens = await ProUserAsync();
        var original = await CreateAsync(tokens.AccessToken, "Referral source", "text");
        await ArchiveAsync(tokens.AccessToken, original);
        await CreateAsync(tokens.AccessToken, "Referral source", "text");

        var unarchive = await _client.UpdateCustomFieldAsync(
            tokens.AccessToken, original.Id, new { label = original.Label, isArchived = false });

        // Both cannot be offered under one name; the one that was away loses.
        await unarchive.ShouldBeProblemAsync(409, "custom_field.label_taken");
    }

    [Fact]
    public async Task Another_users_field_is_a_404_not_a_403()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var created = await CreateAsync(theirs.AccessToken, "Their field", "text");

        await (await _client.GetCustomFieldAsync(mine.AccessToken, created.Id))
            .ShouldBeProblemAsync(404, "custom_field.not_found");

        await (await _client.UpdateCustomFieldAsync(
                mine.AccessToken, created.Id, new { label = "Mine now", isArchived = false }))
            .ShouldBeProblemAsync(404, "custom_field.not_found");
    }

    [Theory]
    [InlineData(null, "text", "label")]
    [InlineData("  ", "text", "label")]
    [InlineData("Fine", "quantum", "type")]
    [InlineData("Fine", null, "type")]
    public async Task A_malformed_definition_is_a_field_keyed_422(string? label, string? type, string field)
    {
        var tokens = await ProUserAsync();

        var response = await _client.CreateCustomFieldAsync(tokens.AccessToken, new { label, type });

        await response.ShouldBeValidationProblemAsync(field);
    }

    [Fact]
    public async Task A_select_without_options_and_a_text_field_with_them_are_both_refused()
    {
        var tokens = await ProUserAsync();

        await (await _client.CreateCustomFieldAsync(
                tokens.AccessToken, new { label = "Team", type = "singleSelect" }))
            .ShouldBeValidationProblemAsync("options");

        await (await _client.CreateCustomFieldAsync(
                tokens.AccessToken, new { label = "Notes", type = "text", options = new[] { "a", "b" } }))
            .ShouldBeValidationProblemAsync("options");
    }

    [Fact]
    public async Task Options_must_differ_from_one_another()
    {
        var tokens = await ProUserAsync();

        await (await _client.CreateCustomFieldAsync(tokens.AccessToken, new
        {
            label = "Team",
            type = "singleSelect",
            options = new[] { "Platform", "platform" },
        })).ShouldBeValidationProblemAsync("options");
    }

    [Fact]
    public async Task Dropping_the_options_from_a_select_on_update_is_refused()
    {
        var tokens = await ProUserAsync();
        var created = await CreateAsync(tokens.AccessToken, "Team", "singleSelect", ["Platform"]);

        // The type is not in the payload, so this rule can only be applied against
        // the stored row - which is exactly what the handler does.
        await (await _client.UpdateCustomFieldAsync(
                tokens.AccessToken, created.Id, new { label = "Team", isArchived = false }))
            .ShouldBeProblemAsync(422, "custom_field.options_invalid");
    }

    [Fact]
    public async Task An_account_may_not_exceed_its_field_budget()
    {
        var tokens = await ProUserAsync();

        for (var index = 0; index < FieldLimit; index++)
        {
            await CreateAsync(tokens.AccessToken, $"Field {index}", "text");
        }

        await (await _client.CreateCustomFieldAsync(
                tokens.AccessToken, new { label = "One too many", type = "text" }))
            .ShouldBeProblemAsync(409, "custom_field.limit_reached");
    }

    [Fact]
    public async Task Archived_fields_still_count_against_the_budget()
    {
        var tokens = await ProUserAsync();

        for (var index = 0; index < FieldLimit; index++)
        {
            var created = await CreateAsync(tokens.AccessToken, $"Field {index}", "text");
            if (index == 0)
            {
                await ArchiveAsync(tokens.AccessToken, created);
            }
        }

        // Archiving does not free a slot: the values recorded against a retired
        // field are kept, so it still occupies a key in every application's bag.
        await (await _client.CreateCustomFieldAsync(
                tokens.AccessToken, new { label = "One too many", type = "text" }))
            .ShouldBeProblemAsync(409, "custom_field.limit_reached");
    }

    /// <summary>A fresh Pro account - defining a field is the paid capability.</summary>
    private Task<AuthTokens> ProUserAsync() => fixture.RegisterProUserAsync(_client, Ct);

    private async Task<CustomFieldView> CreateAsync(
        string? accessToken, string label, string type, string[]? options = null) =>
        await (await _client.CreateCustomFieldAsync(accessToken, new { label, type, options }))
            .ReadCustomFieldAsync();

    private async Task ArchiveAsync(string? accessToken, CustomFieldView definition) =>
        (await _client.UpdateCustomFieldAsync(accessToken, definition.Id, new
        {
            label = definition.Label,
            options = definition.Options,
            isArchived = true,
        })).IsSuccessStatusCode.ShouldBeTrue();
}
