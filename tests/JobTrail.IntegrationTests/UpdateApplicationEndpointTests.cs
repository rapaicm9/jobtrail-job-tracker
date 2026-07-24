using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// <c>PUT /api/v1/applications/{id}</c> against a real database: a full replace of
/// the editable fields that never touches the pipeline stage, guards the
/// offer-decision deadline behind an actual offer, resolves company the same way
/// create does, and stays owner-scoped (a 404 for anyone else's application).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UpdateApplicationEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Replaces_the_editable_fields()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Backend Engineer" });

        var updated = await (await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Staff Backend Engineer",
            companyName = "Acme Corp",
            compensation = new { amount = 150_000m, currency = "usd" },
            location = "Remote",
            workMode = "remote",
            postingUrl = "https://example.com/jobs/99",
            source = "Referral",
            appliedDate = "2026-07-18",
            applicationDeadline = "2026-08-05",
            cvLabel = "cv-v4",
            coverLetterLabel = "cover-v2",
        })).ReadApplicationAsync();

        updated.Role.ShouldBe("Staff Backend Engineer");
        updated.CompanyId.ShouldNotBeNull();
        updated.Compensation.ShouldBe(new MoneyView(150_000m, "USD"));
        updated.Location.ShouldBe("Remote");
        updated.WorkMode.ShouldBe("Remote");
        updated.Source.ShouldBe("Referral");
        updated.AppliedDate.ShouldBe(new DateOnly(2026, 7, 18));
        updated.ApplicationDeadline.ShouldBe(new DateOnly(2026, 8, 5));
        updated.UpdatedAt.ShouldNotBeNull();

        // Persisted, not just echoed. The response carried the full-tick clock
        // value it stamped; the stored timestamp is Postgres microsecond precision,
        // so compare every other field exactly and the timestamp within precision.
        var fetched = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id)).ReadApplicationAsync();
        fetched.ShouldBe(updated with { UpdatedAt = fetched.UpdatedAt });
        fetched.UpdatedAt!.Value.ShouldBe(updated.UpdatedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Clears_a_field_left_off_the_replace()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer", location = "Belgrade" });
        created.Location.ShouldBe("Belgrade");

        var updated = await (await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            appliedDate = created.AppliedDate.ToString("O"),
        })).ReadApplicationAsync();

        updated.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Leaves_the_pipeline_stage_untouched()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });
        await _client.TransitionApplicationAsync(tokens.AccessToken, created.Id, "Screening");

        var updated = await (await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            appliedDate = created.AppliedDate.ToString("O"),
        })).ReadApplicationAsync();

        updated.Stage.ShouldBe("Screening");
    }

    [Fact]
    public async Task Refuses_an_offer_decision_deadline_before_an_offer()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });

        var response = await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            appliedDate = created.AppliedDate.ToString("O"),
            offerDecisionDeadline = "2026-08-15",
        });

        await response.ShouldBeProblemAsync(422, "application.offer_deadline_requires_offer");
    }

    [Fact]
    public async Task Accepts_an_offer_decision_deadline_once_at_offer()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });
        await _client.TransitionApplicationAsync(tokens.AccessToken, created.Id, "Offer");

        var updated = await (await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = "Engineer",
            appliedDate = created.AppliedDate.ToString("O"),
            offerDecisionDeadline = "2026-08-15",
        })).ReadApplicationAsync();

        updated.OfferDecisionDeadline.ShouldBe(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public async Task Rejects_a_company_id_the_caller_does_not_own()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirCompany = (await CreateAsync(theirs.AccessToken, new { role = "Engineer", companyName = "Umbrella" })).CompanyId;
        var mineApp = await CreateAsync(mine.AccessToken, new { role = "Engineer" });

        var response = await _client.UpdateApplicationAsync(mine.AccessToken, mineApp.Id, new
        {
            role = "Engineer",
            appliedDate = mineApp.AppliedDate.ToString("O"),
            companyId = theirCompany,
        });

        await response.ShouldBeProblemAsync(422, "application.unknown_company");
    }

    [Fact]
    public async Task Requires_an_applied_date_and_a_role()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await CreateAsync(tokens.AccessToken, new { role = "Engineer" });

        var response = await _client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new { role = "  " });

        await response.ShouldBeValidationProblemAsync("role", "appliedDate");
    }

    [Fact]
    public async Task Returns_404_for_another_users_application()
    {
        var owner = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var other = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateAsync(owner.AccessToken, new { role = "Engineer" });

        var response = await _client.UpdateApplicationAsync(other.AccessToken, application.Id, new
        {
            role = "Engineer",
            appliedDate = application.AppliedDate.ToString("O"),
        });

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.UpdateApplicationAsync(accessToken: null, Guid.NewGuid(), new { role = "Engineer" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<ApplicationView> CreateAsync(string? accessToken, object body) =>
        await (await _client.CreateApplicationAsync(accessToken, body)).ReadApplicationAsync();
}
