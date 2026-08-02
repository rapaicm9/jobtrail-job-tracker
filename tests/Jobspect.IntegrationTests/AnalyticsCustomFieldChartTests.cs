using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The one panel served from another module, end to end: a Pro account defines a
/// field, answers it across several applications, and asks Analytics to count
/// them.
/// <para>
/// The counting itself is unit-tested where the awkward answers can be enumerated;
/// what is proved here is the whole path - the gate, the Contracts call across the
/// boundary, and the answers a real JSONB column gives back.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsCustomFieldChartTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task A_select_field_is_counted_per_option()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var field = await DefineAsync(tokens, "Referral source", "singleSelect", ["Employee", "Job board"]);

        await CreateAsync(tokens, field, "Employee");
        await CreateAsync(tokens, field, "Employee");
        await CreateAsync(tokens, field, "Job board");
        await CreateAsync(tokens, field, answer: null);

        var chart = await ReadAsync(tokens, field);

        chart.Label.ShouldBe("Referral source");
        chart.Type.ShouldBe("SingleSelect");
        chart.Applications.ShouldBe(4);
        chart.Numbers.ShouldBeNull();
        chart.Periods.ShouldBeNull();

        var categories = chart.Categories.ShouldNotBeNull();
        categories.Select(bucket => (bucket.Value, bucket.Count))
            .ShouldBe([("Employee", 2), ("Job board", 1), (null, 1)]);
    }

    [Fact]
    public async Task A_number_field_comes_back_as_five_values_rather_than_answers()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var field = await DefineAsync(tokens, "Expected salary", "number");

        foreach (var salary in new[] { 60000, 70000, 80000, 90000, 100000 })
        {
            await CreateAsync(tokens, field, salary);
        }

        var chart = await ReadAsync(tokens, field);

        var numbers = chart.Numbers.ShouldNotBeNull();
        numbers.Answered.ShouldBe(5);
        numbers.Minimum.ShouldBe(60000);
        numbers.Median.ShouldBe(80000);
        numbers.Maximum.ShouldBe(100000);

        chart.Categories.ShouldBeNull();
    }

    [Fact]
    public async Task A_date_field_is_counted_by_month()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var field = await DefineAsync(tokens, "Expected start", "date");

        await CreateAsync(tokens, field, "2026-05-04");
        await CreateAsync(tokens, field, "2026-05-25");
        await CreateAsync(tokens, field, "2026-06-01");

        var chart = await ReadAsync(tokens, field);

        var periods = chart.Periods.ShouldNotBeNull();
        periods.Select(period => (period.PeriodStart, period.Count))
            .ShouldBe([(new DateOnly(2026, 5, 1), 2), (new DateOnly(2026, 6, 1), 1)]);
    }

    [Fact]
    public async Task A_text_field_is_answered_as_though_it_were_not_there()
    {
        // Not a 404 for tidiness - free text has no route out of the module, and
        // "there is no chart here" is the only thing this endpoint will ever say
        // about one.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var field = await DefineAsync(tokens, "Interview notes", "text");

        await CreateAsync(tokens, field, "They asked about distributed systems");

        (await _client.GetCustomFieldChartAsync(tokens.AccessToken, field))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_accounts_field_is_not_found()
    {
        var mine = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var notMine = await DefineAsync(theirs, "Their field", "singleSelect", ["A", "B"]);

        await CreateAsync(theirs, notMine, "A");

        (await _client.GetCustomFieldChartAsync(mine.AccessToken, notMine))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_field_is_not_found()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        (await _client.GetCustomFieldChartAsync(tokens.AccessToken, Guid.CreateVersion7()))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_campaign_filter_narrows_the_panel()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var field = await DefineAsync(tokens, "Referral source", "singleSelect", ["Employee", "Job board"]);
        var second = await (await _client.CreateCampaignAsync(
            tokens.AccessToken, new { name = "Second search" })).ReadCampaignAsync();

        await CreateAsync(tokens, field, "Employee");
        await CreateAsync(tokens, field, "Job board", second.Id);

        var narrowed = await ReadAsync(tokens, field, second.Id);

        narrowed.Applications.ShouldBe(1);
        narrowed.Categories.ShouldNotBeNull().ShouldHaveSingleItem().Value.ShouldBe("Job board");
    }

    [Fact]
    public async Task The_panel_is_gated_and_the_definitions_behind_it_are_not()
    {
        var pro = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var field = await DefineAsync(pro, "Referral source", "singleSelect", ["Employee"]);

        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        (await _client.GetCustomFieldChartAsync(free.AccessToken, field))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.GetCustomFieldChartAsync(accessToken: null, field))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Reading definitions stays open, so an account that lost the entitlement
        // can still make sense of what it recorded under them.
        (await _client.ListCustomFieldsAsync(free.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> DefineAsync(
        AuthTokens tokens, string label, string type, string[]? options = null) =>
        (await (await _client.CreateCustomFieldAsync(tokens.AccessToken, new { label, type, options }))
            .ReadCustomFieldAsync()).Id;

    private async Task CreateAsync(
        AuthTokens tokens, Guid fieldId, object? answer, Guid? campaignId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["role"] = "Engineer",
            ["customFields"] = answer is null
                ? null
                : new Dictionary<Guid, object?> { [fieldId] = answer },
        };

        if (campaignId is { } id)
        {
            body["campaignId"] = id;
        }

        (await _client.CreateApplicationAsync(tokens.AccessToken, body))
            .IsSuccessStatusCode.ShouldBeTrue();
    }

    private async Task<CustomFieldChartView> ReadAsync(
        AuthTokens tokens, Guid definitionId, Guid? campaignId = null) =>
        await (await _client.GetCustomFieldChartAsync(tokens.AccessToken, definitionId, campaignId))
            .ReadCustomFieldChartAsync();
}
