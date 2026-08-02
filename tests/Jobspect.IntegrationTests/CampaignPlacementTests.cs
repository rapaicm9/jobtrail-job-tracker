using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Which campaign an application belongs to, end to end: chosen when it is opened,
/// changed by a replace, and used to narrow the list.
/// <para>
/// None of it is gated. Holding more than one campaign is the paid capability;
/// putting an application in a campaign the account already holds is not, which is
/// what lets a downgraded account go on curating the campaigns it kept.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CampaignPlacementTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task An_application_opens_in_the_campaign_the_request_names()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "2026 backend roles");

        var created = await CreateApplicationAsync(tokens, new { role = "Engineer", campaignId = campaign.Id });

        // Placed on the way in, not moved afterwards.
        created.CampaignId.ShouldBe(campaign.Id);

        var fetched = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id))
            .ReadApplicationAsync();
        fetched.CampaignId.ShouldBe(campaign.Id);
    }

    [Fact]
    public async Task An_application_that_names_no_campaign_opens_in_the_default()
    {
        var tokens = await ProUserAsync();
        var defaultId = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);
        await CreateCampaignAsync(tokens, "A campaign it should not land in");

        var created = await CreateApplicationAsync(tokens, new { role = "Engineer" });

        created.CampaignId.ShouldBe(defaultId);
    }

    [Fact]
    public async Task Opening_an_application_in_another_users_campaign_is_refused()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var theirCampaign = await CreateCampaignAsync(theirs, "Not yours");

        // A 422 about the request body, not a 404 about the campaign - the latter
        // would confirm whose it is.
        await (await _client.CreateApplicationAsync(
                mine.AccessToken, new { role = "Engineer", campaignId = theirCampaign.Id }))
            .ShouldBeProblemAsync(422, "application.unknown_campaign");
    }

    [Fact]
    public async Task A_replace_moves_the_application_to_another_campaign()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "Contract work");
        var created = await CreateApplicationAsync(tokens, new { role = "Engineer" });

        var moved = await (await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = campaign.Id,
            appliedDate = created.AppliedDate.ToString("O"),
        })).ReadApplicationAsync();

        moved.CampaignId.ShouldBe(campaign.Id);

        var fetched = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id))
            .ReadApplicationAsync();
        fetched.CampaignId.ShouldBe(campaign.Id);
    }

    [Fact]
    public async Task A_replace_into_another_users_campaign_is_refused()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var theirCampaign = await CreateCampaignAsync(theirs, "Not yours either");
        var created = await CreateApplicationAsync(mine, new { role = "Engineer" });

        await (await _client.UpdateApplicationAsync(mine.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = theirCampaign.Id,
            appliedDate = created.AppliedDate.ToString("O"),
        })).ShouldBeProblemAsync(422, "application.unknown_campaign");
    }

    [Fact]
    public async Task The_list_narrows_to_one_campaign()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "Only these");
        var inside = await CreateApplicationAsync(tokens, new { role = "Inside", campaignId = campaign.Id });
        var outside = await CreateApplicationAsync(tokens, new { role = "Outside" });

        var narrowed = await (await _client.ListApplicationsAsync(tokens.AccessToken, campaignId: campaign.Id))
            .ReadApplicationListAsync();

        narrowed.Select(a => a.Id).ShouldBe([inside.Id]);

        // And the unnarrowed list still holds both.
        var everything = await (await _client.ListApplicationsAsync(tokens.AccessToken)).ReadApplicationListAsync();
        everything.Select(a => a.Id).ShouldBe([outside.Id, inside.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Listing_by_another_users_campaign_is_refused_rather_than_answered_empty()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var theirCampaign = await CreateCampaignAsync(theirs, "Theirs to list");

        // An empty page already means "nothing matched". A campaign that is not
        // yours has to read differently, or a mistyped id looks like an empty
        // campaign forever.
        await (await _client.ListApplicationsAsync(mine.AccessToken, campaignId: theirCampaign.Id))
            .ShouldBeProblemAsync(422, "application.unknown_campaign");
    }

    [Fact]
    public async Task A_campaign_narrows_a_custom_field_filter_too()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "Filtered");
        var field = await DefineFieldAsync(tokens, "Priority", "text");

        var wanted = await CreateApplicationAsync(tokens, new
        {
            role = "Wanted",
            campaignId = campaign.Id,
            customFields = Answer(field.Id, "high"),
        });

        // Same answer, other campaign - excluded by the campaign, not the filter.
        await CreateApplicationAsync(tokens, new { role = "Elsewhere", customFields = Answer(field.Id, "high") });

        // Same campaign, other answer - excluded by the filter, not the campaign.
        await CreateApplicationAsync(tokens, new
        {
            role = "Other answer",
            campaignId = campaign.Id,
            customFields = Answer(field.Id, "low"),
        });

        var narrowed = await (await _client.ListApplicationsAsync(
                tokens.AccessToken, campaignId: campaign.Id, customFieldId: field.Id, customFieldValue: "high"))
            .ReadApplicationListAsync();

        narrowed.Select(a => a.Id).ShouldBe([wanted.Id]);
    }

    [Fact]
    public async Task A_campaign_narrows_a_custom_field_sort_too()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "Sorted");
        var field = await DefineFieldAsync(tokens, "Rank", "number");

        var first = await CreateApplicationAsync(tokens, new
        {
            role = "First",
            campaignId = campaign.Id,
            customFields = Answer(field.Id, 1),
        });

        var second = await CreateApplicationAsync(tokens, new
        {
            role = "Second",
            campaignId = campaign.Id,
            customFields = Answer(field.Id, 2),
        });

        await CreateApplicationAsync(tokens, new { role = "Elsewhere", customFields = Answer(field.Id, 3) });

        // A sort is written as SQL rather than composed by LINQ, so it is a second
        // place the campaign has to be applied. Miss it and the ordering is right
        // while the rows are wrong - the failure this test exists for.
        var sorted = await (await _client.ListApplicationsAsync(
                tokens.AccessToken,
                campaignId: campaign.Id,
                sortCustomFieldId: field.Id,
                sortDirection: "asc"))
            .ReadApplicationListAsync();

        sorted.Select(a => a.Id).ShouldBe([first.Id, second.Id]);
    }

    /// <summary>A Pro account with its default campaign - it needs Pro to open a second one.</summary>
    private Task<AuthTokens> ProUserAsync() => fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

    private async Task<CampaignView> CreateCampaignAsync(AuthTokens tokens, string name) =>
        await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name })).ReadCampaignAsync();

    private async Task<ApplicationView> CreateApplicationAsync(AuthTokens tokens, object body) =>
        await (await _client.CreateApplicationAsync(tokens.AccessToken, body)).ReadApplicationAsync();

    private async Task<CustomFieldView> DefineFieldAsync(AuthTokens tokens, string label, string type) =>
        await (await _client.CreateCustomFieldAsync(tokens.AccessToken, new { label, type }))
            .ReadCustomFieldAsync();

    /// <summary>One custom-field answer on the wire: keyed by definition id, not by name.</summary>
    private static Dictionary<string, object?> Answer(Guid fieldId, object value) =>
        new() { [fieldId.ToString()] = value };
}
