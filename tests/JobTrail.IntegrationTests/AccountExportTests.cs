using System.Net;
using System.Text.Json;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/account/export</c> against a real database: everything the
/// system holds for one account, gathered from every module into one downloadable
/// document.
/// <para>
/// Three claims are load-bearing here. The document reaches across module
/// boundaries without any module reading another's tables - each contributes its
/// own section. It carries only that account's data. And it carries none of the
/// account's secrets: the password hash and the refresh tokens live in the same
/// module as the profile, and this is the one endpoint that hands a file to the
/// user to keep, forward and store.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountExportTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Exports_every_module_that_holds_the_accounts_data()
    {
        var tokens = await ProUserAsync();
        await SeedAJobSearchAsync(tokens);

        var document = await ExportAsync(tokens);

        // One section per module that holds anything, plus enough for a reader to
        // know whose file this is and when it was taken.
        document.TryGetProperty("exportedAt", out _).ShouldBeTrue();
        document.GetProperty("accountId").GetGuid().ShouldBe(tokens.UserId);
        document.TryGetProperty("identity", out _).ShouldBeTrue();
        document.TryGetProperty("applications", out _).ShouldBeTrue();
        document.TryGetProperty("billing", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Carries_the_profile_the_user_gave_us()
    {
        var tokens = await ProUserAsync();

        var identity = (await ExportAsync(tokens)).GetProperty("identity");

        identity.GetProperty("email").GetString().ShouldNotBeNullOrWhiteSpace();
        identity.GetProperty("timeZoneId").GetString().ShouldNotBeNullOrWhiteSpace();
        identity.TryGetProperty("createdAt", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Carries_the_whole_record_of_the_job_search()
    {
        var tokens = await ProUserAsync();
        var seeded = await SeedAJobSearchAsync(tokens);

        var applications = (await ExportAsync(tokens)).GetProperty("applications");

        // Every section the module holds, and the user's own words among them -
        // the note on the timeline is the least reconstructible thing here.
        Ids(applications, "campaigns").ShouldContain(seeded.CampaignId);
        Ids(applications, "companies").ShouldContain(seeded.CompanyId);
        Ids(applications, "customFields").ShouldContain(seeded.CustomFieldId);
        Ids(applications, "applications").ShouldContain(seeded.ApplicationId);
        Ids(applications, "contacts").ShouldContain(seeded.ContactId);
        Ids(applications, "interviews").ShouldContain(seeded.InterviewId);

        applications.GetProperty("activity").EnumerateArray()
            .Select(entry => entry.GetProperty("note").ValueKind is JsonValueKind.String
                ? entry.GetProperty("note").GetString()
                : null)
            .ShouldContain("Recruiter said they would call back on Monday");

        // The application carries its answers, keyed by the definition that
        // explains them - which is why the definitions travel too.
        var application = applications.GetProperty("applications").EnumerateArray()
            .Single(a => a.GetProperty("id").GetGuid() == seeded.ApplicationId);

        application.GetProperty("role").GetString().ShouldBe("Staff Backend Engineer");
        application.GetProperty("customFields")
            .GetProperty(seeded.CustomFieldId.ToString()).GetString().ShouldBe("Referred by a friend");
    }

    [Fact]
    public async Task Carries_the_plan_and_the_purchase_behind_it()
    {
        var tokens = await ProUserAsync();

        var billing = (await ExportAsync(tokens)).GetProperty("billing");

        billing.GetProperty("plan").GetProperty("tier").GetString().ShouldBe("Pro");
        billing.GetProperty("purchases").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Carries_none_of_the_accounts_secrets()
    {
        var tokens = await ProUserAsync();
        await SeedAJobSearchAsync(tokens);

        var raw = await RawExportAsync(tokens);

        // The refresh token the caller is holding right now, and the hash of the
        // password they registered with. Both live in the module that wrote the
        // profile section, which is exactly why this is asserted rather than assumed.
        raw.ShouldNotContain(tokens.RefreshToken);
        raw.ShouldNotContain(ApiClient.Password);
        raw.ShouldNotContain("passwordHash", Case.Insensitive);
        raw.ShouldNotContain("securityStamp", Case.Insensitive);
        raw.ShouldNotContain("tokenVersion", Case.Insensitive);
    }

    [Fact]
    public async Task Carries_nobody_elses_data()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var theirSearch = await SeedAJobSearchAsync(theirs);
        await SeedAJobSearchAsync(mine);

        var raw = await RawExportAsync(mine);

        // Ownership is inside every exporter's query, so nothing of theirs can
        // appear - not the ids, and not the words they typed.
        raw.ShouldNotContain(theirSearch.ApplicationId.ToString());
        raw.ShouldNotContain(theirSearch.CompanyId.ToString());
        raw.ShouldNotContain(theirSearch.ContactId.ToString());
    }

    [Fact]
    public async Task An_empty_account_exports_empty_sections_rather_than_failing()
    {
        // A brand-new Pro account: a default campaign and a plan, and nothing else.
        var tokens = await ProUserAsync();

        var applications = (await ExportAsync(tokens)).GetProperty("applications");

        applications.GetProperty("applications").GetArrayLength().ShouldBe(0);
        applications.GetProperty("interviews").GetArrayLength().ShouldBe(0);
        applications.GetProperty("customFields").GetArrayLength().ShouldBe(0);

        // The campaign every account is given is still there - "empty" is not "no
        // account".
        applications.GetProperty("campaigns").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Comes_back_as_a_dated_json_download()
    {
        var tokens = await ProUserAsync();

        var response = await _client.ExportAccountAsync(tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        // A file the user saves, not a body a browser renders.
        var disposition = response.Content.Headers.ContentDisposition.ShouldNotBeNull();
        disposition.DispositionType.ShouldBe("attachment");
        disposition.FileName.ShouldNotBeNull().ShouldStartWith("jobtrail-export-");
        disposition.FileName.ShouldEndWith(".json");
    }

    [Fact]
    public async Task Free_is_forbidden_and_an_anonymous_caller_is_challenged()
    {
        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // Waiting for the plan means the 403 is the entitlement being absent
        // rather than the plan not existing yet.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(UserId.From(free.UserId), Ct) is not null,
            "registration should provision the Free plan the gate reads",
            Ct);

        // Exporting is Pro because it produces a copy and traps nothing. Erasure,
        // which destroys, stays free - a user never pays to leave.
        (await _client.ExportAccountAsync(free.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.ExportAccountAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.DeleteAccountAsync(free.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task<AuthTokens> ProUserAsync() => fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

    private async Task<JsonElement> ExportAsync(AuthTokens tokens) =>
        JsonDocument.Parse(await RawExportAsync(tokens)).RootElement;

    private async Task<string> RawExportAsync(AuthTokens tokens)
    {
        var response = await _client.ExportAccountAsync(tokens.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static IEnumerable<Guid> Ids(JsonElement section, string name) =>
        section.GetProperty(name).EnumerateArray().Select(row => row.GetProperty("id").GetGuid());

    /// <summary>
    /// One of everything the Applications module can hold, so a section that stops
    /// being exported has something to be missing.
    /// </summary>
    private async Task<SeededSearch> SeedAJobSearchAsync(AuthTokens tokens)
    {
        var campaign = await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name = "2026 roles" }))
            .ReadCampaignAsync();

        var field = await (await _client.CreateCustomFieldAsync(
            tokens.AccessToken, new { label = "How I found it", type = "text" })).ReadCustomFieldAsync();

        var application = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Staff Backend Engineer",
            campaignId = campaign.Id,
            companyName = "Acme Corp",
            location = "Belgrade",
            customFields = new Dictionary<string, object?> { [field.Id.ToString()] = "Referred by a friend" },
        })).ReadApplicationAsync();

        var contact = await (await _client.CreateContactAsync(tokens.AccessToken, new
        {
            applicationId = application.Id,
            name = "Dana Reeves",
            role = "Recruiter",
            email = "dana@example.com",
        })).ReadContactAsync();

        var interview = await (await _client.CreateInterviewAsync(tokens.AccessToken, application.Id, new
        {
            scheduledAt = "2026-09-01T10:00:00Z",
            type = "PhoneScreen",
            format = "Remote",
        })).ReadInterviewAsync();

        (await _client.AddNoteAsync(tokens.AccessToken, application.Id, new
        {
            note = "Recruiter said they would call back on Monday",
        })).IsSuccessStatusCode.ShouldBeTrue();

        return new SeededSearch(
            campaign.Id, application.CompanyId!.Value, field.Id, application.Id, contact.Id, interview.Id);
    }

    private sealed record SeededSearch(
        Guid CampaignId, Guid CompanyId, Guid CustomFieldId, Guid ApplicationId, Guid ContactId, Guid InterviewId);
}
