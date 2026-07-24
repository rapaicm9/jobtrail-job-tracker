using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/applications</c> against a real database: an application opens
/// in the caller's default campaign at <c>Applied</c>, its built-in fields
/// round-trip, the company follows the picker's reference-or-create modes, and the
/// request is validated to a field-keyed 422. The owner is always the token's
/// subject, never the body.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CreateApplicationEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Creates_an_application_and_returns_it()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateApplicationAsync(tokens.AccessToken, new { role = "Backend Engineer" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadApplicationAsync();
        created.Id.ShouldNotBe(Guid.Empty);
        created.Role.ShouldBe("Backend Engineer");
        created.Stage.ShouldBe("Applied");
        created.CompanyId.ShouldBeNull();
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/applications/{created.Id}");

        // Readable straight back through the get route.
        var fetched = await (await _client.GetApplicationAsync(tokens.AccessToken, created.Id)).ReadApplicationAsync();
        fetched.ShouldBe(created);
    }

    [Fact]
    public async Task Places_the_application_in_the_callers_default_campaign()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var defaultCampaign = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);

        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Backend Engineer" })).ReadApplicationAsync();

        created.CampaignId.ShouldBe(defaultCampaign);
    }

    [Fact]
    public async Task Round_trips_every_built_in_field()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var body = new
        {
            role = "Staff Backend Engineer",
            companyName = "Acme Corp",
            compensation = new { amount = 120_000.50m, currency = "eur" },
            location = "Belgrade",
            workMode = "remote",
            postingUrl = "https://example.com/jobs/42",
            source = "LinkedIn",
            appliedDate = "2026-07-20",
            applicationDeadline = "2026-08-01",
            cvLabel = "cv-backend-v3",
            coverLetterLabel = "cover-acme",
        };

        var created = await (await _client.CreateApplicationAsync(tokens.AccessToken, body)).ReadApplicationAsync();

        created.Role.ShouldBe("Staff Backend Engineer");
        created.CompanyId.ShouldNotBeNull();
        created.Compensation.ShouldBe(new MoneyView(120_000.50m, "EUR")); // currency normalised to upper case
        created.Location.ShouldBe("Belgrade");
        created.WorkMode.ShouldBe("Remote");
        created.PostingUrl.ShouldBe("https://example.com/jobs/42");
        created.Source.ShouldBe("LinkedIn");
        created.AppliedDate.ShouldBe(new DateOnly(2026, 7, 20));
        created.ApplicationDeadline.ShouldBe(new DateOnly(2026, 8, 1));
        created.OfferDecisionDeadline.ShouldBeNull();
        created.CvLabel.ShouldBe("cv-backend-v3");
        created.CoverLetterLabel.ShouldBe("cover-acme");

        // The named company was created and is now offered by the picker.
        var companies = await (await _client.SearchCompaniesAsync(tokens.AccessToken, "acme")).ReadCompaniesAsync();
        companies.ShouldHaveSingleItem().Id.ShouldBe(created.CompanyId!.Value);
    }

    [Fact]
    public async Task Defaults_the_applied_date_to_the_callers_local_today()
    {
        // A zone far ahead of UTC, so "today" there is a deliberate choice, not the
        // server's incidental clock.
        const string zone = "Asia/Tokyo";
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct, timeZoneId: zone);

        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Backend Engineer" })).ReadApplicationAsync();

        var tokyoToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        created.AppliedDate.ShouldBe(tokyoToday);
    }

    [Fact]
    public async Task Reuses_an_existing_company_when_created_by_the_same_name()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var first = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer I", companyName = "Globex" })).ReadApplicationAsync();
        var second = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer II", companyName = "globex" })).ReadApplicationAsync();

        // Case-insensitive exact match: the same company, not a duplicate.
        second.CompanyId.ShouldBe(first.CompanyId);
        (await (await _client.SearchCompaniesAsync(tokens.AccessToken, "globex")).ReadCompaniesAsync())
            .Count.ShouldBe(1);
    }

    [Fact]
    public async Task References_an_existing_company_by_id()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var seeded = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer", companyName = "Initech" })).ReadApplicationAsync();

        var referencing = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Senior Engineer", companyId = seeded.CompanyId })).ReadApplicationAsync();

        referencing.CompanyId.ShouldBe(seeded.CompanyId);
    }

    [Fact]
    public async Task Rejects_a_company_id_the_caller_does_not_own()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirCompany = (await (await _client.CreateApplicationAsync(
            theirs.AccessToken, new { role = "Engineer", companyName = "Umbrella" })).ReadApplicationAsync()).CompanyId;

        var response = await _client.CreateApplicationAsync(
            mine.AccessToken, new { role = "Engineer", companyId = theirCompany });

        await response.ShouldBeProblemAsync(422, "application.unknown_company");
    }

    [Fact]
    public async Task Rejects_both_a_company_id_and_a_company_name()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer", companyId = Guid.NewGuid(), companyName = "Acme" });

        await response.ShouldBeValidationProblemAsync("companyName");
    }

    [Fact]
    public async Task Requires_a_role()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateApplicationAsync(tokens.AccessToken, new { role = "   " });

        await response.ShouldBeValidationProblemAsync("role");
    }

    [Fact]
    public async Task Rejects_malformed_fields()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Engineer",
            workMode = "Hybridish",
            postingUrl = "not-a-url",
            compensation = new { amount = -5m, currency = "euro" },
        });

        await response.ShouldBeValidationProblemAsync("workMode", "postingUrl", "compensation");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.CreateApplicationAsync(accessToken: null, new { role = "Engineer" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
