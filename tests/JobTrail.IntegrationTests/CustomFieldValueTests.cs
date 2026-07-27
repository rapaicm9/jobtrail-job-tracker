using System.Net;
using System.Text.Json;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The answers an application holds to its account's own fields, against a real
/// database: every type survives the round trip as the JSON it went in as, the
/// answers are checked against the definitions that give them meaning, and writing
/// them is Pro while reading them back is not.
/// <para>
/// The replace rules are the substance here. <c>PUT</c> is a full replace, so a bag
/// left off clears - for a caller entitled to write it. For one who is not, the
/// bag is untouchable: sending it is refused, and leaving it off leaves the stored
/// answers alone. That second case is what "retained read-only" actually means,
/// and without it an account that lost the entitlement would drain its own answers
/// away one edit at a time.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomFieldValueTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Every_type_survives_the_round_trip_as_the_json_it_arrived_as()
    {
        var tokens = await ProUserAsync();

        var text = await DefineAsync(tokens, "Referral source", "text");
        var number = await DefineAsync(tokens, "Priority", "number");
        var date = await DefineAsync(tokens, "Follow up on", "date");
        var checkbox = await DefineAsync(tokens, "Remote ok", "checkbox");
        var single = await DefineAsync(tokens, "Team", "singleSelect", ["Platform", "Product"]);
        var multi = await DefineAsync(tokens, "Stack", "multiSelect", ["Go", "Rust", "C#"]);
        var url = await DefineAsync(tokens, "Take-home", "url");

        var created = await CreateAsync(tokens, Values(
            (text.Id, "LinkedIn"),
            (number.Id, 3),
            (date.Id, "2026-09-01"),
            (checkbox.Id, true),
            (single.Id, "Platform"),
            (multi.Id, new[] { "Go", "Rust" }),
            (url.Id, "https://example.com/task")));

        // A string stays a string, a number stays a number, an array stays an
        // array. Stored raw rather than wrapped, so the document is what a person
        // would write - and what a JSONB path can address a single field of.
        created.CustomFields[text.Id].GetString().ShouldBe("LinkedIn");
        created.CustomFields[number.Id].GetInt32().ShouldBe(3);
        created.CustomFields[date.Id].GetString().ShouldBe("2026-09-01");
        created.CustomFields[checkbox.Id].GetBoolean().ShouldBeTrue();
        created.CustomFields[single.Id].GetString().ShouldBe("Platform");
        created.CustomFields[multi.Id].GetRawText().ShouldBe("""["Go","Rust"]""");
        created.CustomFields[url.Id].GetString().ShouldBe("https://example.com/task");

        // And again off a fresh read, so this is the database's answer rather than
        // the one the write happened to still be holding.
        var read = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id)).ReadApplicationAsync();
        read.CustomFields[number.Id].GetInt32().ShouldBe(3);
        read.CustomFields[multi.Id].GetRawText().ShouldBe("""["Go","Rust"]""");
    }

    [Fact]
    public async Task An_application_without_answers_carries_an_empty_bag()
    {
        var tokens = await ProUserAsync();

        var created = await CreateAsync(tokens, customFields: null);

        // Always present, never null - a client never has to tell "no answers"
        // from "the server didn't say".
        created.CustomFields.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_answer_is_not_an_answer()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");

        var created = await CreateAsync(tokens, Values((field.Id, null)));

        // Dropped rather than stored as JSON null: the document holds answers, and
        // this is how a client clears one field without clearing the rest.
        created.CustomFields.ShouldBeEmpty();
    }

    [Fact]
    public async Task Writing_answers_without_the_entitlement_is_forbidden()
    {
        var pro = await ProUserAsync();
        var field = await DefineAsync(pro, "Referral source", "text");

        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateApplicationAsync(
            free.AccessToken,
            new { role = "Engineer", customFields = Values((field.Id, "LinkedIn")) });

        // 403, not 422: the request is understood and the caller is known, they
        // simply may not write this part of it.
        await response.ShouldBeProblemAsync(403, "custom_field.not_entitled");
    }

    [Fact]
    public async Task An_account_without_the_entitlement_still_opens_applications()
    {
        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // The gate is on the bag, not the endpoint - both tiers record applications.
        (await _client.CreateApplicationAsync(free.AccessToken, new { role = "Engineer" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_entitled_edit_replaces_the_answers()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        var created = await CreateAsync(tokens, Values((field.Id, "LinkedIn")));

        var updated = await UpdateAsync(tokens, created.Id, Values((field.Id, "Referral")));

        updated.CustomFields[field.Id].GetString().ShouldBe("Referral");
    }

    [Fact]
    public async Task An_entitled_edit_that_leaves_the_bag_off_clears_it()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        var created = await CreateAsync(tokens, Values((field.Id, "LinkedIn")));

        // A replace is a replace: the bag is not special for a caller who may
        // write it, and an omitted field is cleared like any other.
        var updated = await UpdateAsync(tokens, created.Id, customFields: null);

        updated.CustomFields.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_edit_without_the_entitlement_leaves_the_answers_alone()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        var created = await CreateAsync(tokens, Values((field.Id, "LinkedIn")));

        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        // The same edit that clears the bag for an entitled caller must not touch
        // it for one who cannot write it - otherwise "retained" would last exactly
        // until the next time the user fixed a typo in the role.
        var updated = await UpdateAsync(tokens, created.Id, customFields: null);

        updated.CustomFields[field.Id].GetString().ShouldBe("LinkedIn");
    }

    [Fact]
    public async Task An_edit_that_sends_answers_without_the_entitlement_is_forbidden()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        var created = await CreateAsync(tokens, Values((field.Id, "LinkedIn")));

        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        await (await UpdateRequestAsync(tokens, created.Id, Values((field.Id, "Referral"))))
            .ShouldBeProblemAsync(403, "custom_field.not_entitled");

        // Clearing is a write too, so an empty bag is refused just the same.
        await (await UpdateRequestAsync(tokens, created.Id, new Dictionary<string, object?>()))
            .ShouldBeProblemAsync(403, "custom_field.not_entitled");
    }

    [Fact]
    public async Task Answers_are_still_read_back_without_the_entitlement()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Referral source", "text");
        var created = await CreateAsync(tokens, Values((field.Id, "LinkedIn")));

        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        var read = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id)).ReadApplicationAsync();

        // Read-only, but read: the answers are meaningless to their owner if the
        // application they hang off stops reporting them.
        read.CustomFields[field.Id].GetString().ShouldBe("LinkedIn");
    }

    [Fact]
    public async Task An_answer_to_a_field_the_caller_does_not_have_is_refused()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var theirField = await DefineAsync(theirs, "Their field", "text");

        // Another user's field and a field that never existed read the same way -
        // the lookup is owner-scoped, so it is simply not theirs to answer.
        await (await _client.CreateApplicationAsync(
                mine.AccessToken,
                new { role = "Engineer", customFields = Values((theirField.Id, "x")) }))
            .ShouldBeProblemAsync(422, "custom_field.unknown_field");

        await (await _client.CreateApplicationAsync(
                mine.AccessToken,
                new { role = "Engineer", customFields = Values((Guid.CreateVersion7(), "x")) }))
            .ShouldBeProblemAsync(422, "custom_field.unknown_field");
    }

    [Fact]
    public async Task An_archived_field_takes_no_new_answers_but_keeps_the_ones_it_has()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Old process", "text");
        var created = await CreateAsync(tokens, Values((field.Id, "recorded while live")));

        (await _client.UpdateCustomFieldAsync(
            tokens.AccessToken, field.Id, new { label = field.Label, isArchived = true }))
            .IsSuccessStatusCode.ShouldBeTrue();

        // Retired means no new answers...
        await (await _client.CreateApplicationAsync(
                tokens.AccessToken,
                new { role = "Engineer", customFields = Values((field.Id, "recorded after")) }))
            .ShouldBeProblemAsync(422, "custom_field.archived_field");

        // ...and everything already recorded stays exactly where it was.
        var read = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id)).ReadApplicationAsync();
        read.CustomFields[field.Id].GetString().ShouldBe("recorded while live");
    }

    [Theory]
    [InlineData("number", "\"twelve\"")]
    [InlineData("checkbox", "\"yes\"")]
    [InlineData("date", "\"01/02/2026\"")]
    [InlineData("text", "12")]
    [InlineData("url", "\"not-a-url\"")]
    [InlineData("multiSelect", "\"Go\"")]
    public async Task An_answer_of_the_wrong_shape_is_refused(string type, string rawJson)
    {
        var tokens = await ProUserAsync();
        var options = type is "multiSelect" ? new[] { "Go" } : null;
        var field = await DefineAsync(tokens, $"Field {type}", type, options);

        var response = await _client.CreateApplicationAsync(
            tokens.AccessToken,
            new { role = "Engineer", customFields = Values((field.Id, JsonDocument.Parse(rawJson).RootElement)) });

        await response.ShouldBeProblemAsync(422, "custom_field.value_invalid");
    }

    [Fact]
    public async Task A_choice_the_field_does_not_offer_is_refused()
    {
        var tokens = await ProUserAsync();
        var single = await DefineAsync(tokens, "Team", "singleSelect", ["Platform", "Product"]);
        var multi = await DefineAsync(tokens, "Stack", "multiSelect", ["Go", "Rust"]);

        await (await _client.CreateApplicationAsync(
                tokens.AccessToken,
                new { role = "Engineer", customFields = Values((single.Id, "Design")) }))
            .ShouldBeProblemAsync(422, "custom_field.unknown_option");

        await (await _client.CreateApplicationAsync(
                tokens.AccessToken,
                new { role = "Engineer", customFields = Values((multi.Id, new[] { "Go", "COBOL" })) }))
            .ShouldBeProblemAsync(422, "custom_field.unknown_option");
    }

    [Fact]
    public async Task An_over_long_answer_is_refused()
    {
        var tokens = await ProUserAsync();
        var field = await DefineAsync(tokens, "Notes", "text");

        var response = await _client.CreateApplicationAsync(
            tokens.AccessToken,
            new { role = "Engineer", customFields = Values((field.Id, new string('x', 2001))) });

        await response.ShouldBeProblemAsync(422, "custom_field.value_invalid");
    }

    /// <summary>A Pro account with the default campaign an application needs.</summary>
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

    private async Task<ApplicationView> CreateAsync(AuthTokens tokens, Dictionary<string, object?>? customFields) =>
        await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer", customFields })).ReadApplicationAsync();

    private async Task<ApplicationView> UpdateAsync(
        AuthTokens tokens, Guid id, Dictionary<string, object?>? customFields) =>
        await (await UpdateRequestAsync(tokens, id, customFields)).ReadApplicationAsync();

    private Task<HttpResponseMessage> UpdateRequestAsync(
        AuthTokens tokens, Guid id, Dictionary<string, object?>? customFields) =>
        _client.UpdateApplicationAsync(tokens.AccessToken, id, new
        {
            role = "Engineer",
            appliedDate = "2026-07-20",
            customFields,
        });

    /// <summary>
    /// A custom-field bag as it goes on the wire: keyed by definition id, each
    /// value the raw JSON its type calls for. Built as a dictionary rather than an
    /// anonymous object because the keys are ids, not names.
    /// </summary>
    private static Dictionary<string, object?> Values(params (Guid Field, object? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Field.ToString(), entry => entry.Value);
}
