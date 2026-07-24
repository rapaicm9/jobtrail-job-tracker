using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The contact slices under <c>/api/v1/contacts</c> against a real database: a
/// contact links to an application and/or a company (at least one, both the
/// caller's own), its fields round-trip, the list is owner-scoped and filterable,
/// and every read and write is a 404 for anyone else's contact.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ContactEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Creates_a_contact_linked_to_an_application()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);

        var response = await _client.CreateContactAsync(tokens.AccessToken, new { applicationId = appId, name = "Alice Recruiter" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadContactAsync();
        created.Id.ShouldNotBe(Guid.Empty);
        created.ApplicationId.ShouldBe(appId);
        created.CompanyId.ShouldBeNull();
        created.Name.ShouldBe("Alice Recruiter");
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/contacts/{created.Id}");

        var fetched = await (await _client.GetContactAsync(tokens.AccessToken, created.Id)).ReadContactAsync();
        fetched.ShouldBe(created);
    }

    [Fact]
    public async Task Round_trips_every_field_on_a_company_contact()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var (_, companyId) = await SeedApplicationWithCompanyAsync(tokens.AccessToken);

        var created = await (await _client.CreateContactAsync(tokens.AccessToken, new
        {
            companyId,
            name = "Bob Manager",
            role = "hiringmanager",
            email = "bob@example.com",
            phone = "+1 (555) 123-4567",
            notes = "Met at the meetup.",
        })).ReadContactAsync();

        created.ApplicationId.ShouldBeNull();
        created.CompanyId.ShouldBe(companyId);
        created.Role.ShouldBe("HiringManager"); // parsed case-insensitively, stored as the canonical name
        created.Email.ShouldBe("bob@example.com");
        created.Phone.ShouldBe("+1 (555) 123-4567");
        created.Notes.ShouldBe("Met at the meetup.");
    }

    [Fact]
    public async Task Links_to_both_an_application_and_a_company()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var (appId, companyId) = await SeedApplicationWithCompanyAsync(tokens.AccessToken);

        var created = await (await _client.CreateContactAsync(
            tokens.AccessToken, new { applicationId = appId, companyId, name = "Carol" })).ReadContactAsync();

        created.ApplicationId.ShouldBe(appId);
        created.CompanyId.ShouldBe(companyId);
    }

    [Fact]
    public async Task Requires_at_least_one_link()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateContactAsync(tokens.AccessToken, new { name = "Nobody" });

        await response.ShouldBeValidationProblemAsync("applicationId");
    }

    [Fact]
    public async Task Rejects_malformed_fields()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var response = await _client.CreateContactAsync(tokens.AccessToken, new
        {
            applicationId = Guid.NewGuid(),
            name = "   ",
            role = "Boss",
            email = "not-an-email",
            phone = "abc",
        });

        await response.ShouldBeValidationProblemAsync("name", "role", "email", "phone");
    }

    [Fact]
    public async Task Rejects_an_application_the_caller_does_not_own()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken);

        var response = await _client.CreateContactAsync(mine.AccessToken, new { applicationId = theirApp, name = "Sneaky" });

        await response.ShouldBeProblemAsync(422, "contact.unknown_application");
    }

    [Fact]
    public async Task Rejects_a_company_the_caller_does_not_own()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var (_, theirCompany) = await SeedApplicationWithCompanyAsync(theirs.AccessToken);

        var response = await _client.CreateContactAsync(mine.AccessToken, new { companyId = theirCompany, name = "Sneaky" });

        await response.ShouldBeProblemAsync(422, "contact.unknown_company");
    }

    [Fact]
    public async Task Lists_the_callers_contacts_ordered_by_name()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(tokens.AccessToken);
        await _client.CreateContactAsync(tokens.AccessToken, new { applicationId = appId, name = "Charlie" });
        await _client.CreateContactAsync(tokens.AccessToken, new { applicationId = appId, name = "Alice" });
        await _client.CreateContactAsync(tokens.AccessToken, new { applicationId = appId, name = "Bob" });

        var contacts = await (await _client.ListContactsAsync(tokens.AccessToken)).ReadContactListAsync();

        contacts.Select(c => c.Name).ShouldBe(["Alice", "Bob", "Charlie"]);
    }

    [Fact]
    public async Task Filters_the_list_by_application_and_by_company()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var (appId, companyId) = await SeedApplicationWithCompanyAsync(tokens.AccessToken);
        var otherApp = await SeedApplicationAsync(tokens.AccessToken);
        await _client.CreateContactAsync(tokens.AccessToken, new { applicationId = appId, companyId, name = "On Both" });
        await _client.CreateContactAsync(tokens.AccessToken, new { applicationId = otherApp, name = "On Other App" });

        var byApp = await (await _client.ListContactsAsync(tokens.AccessToken, applicationId: appId)).ReadContactListAsync();
        var byCompany = await (await _client.ListContactsAsync(tokens.AccessToken, companyId: companyId)).ReadContactListAsync();

        byApp.ShouldHaveSingleItem().Name.ShouldBe("On Both");
        byCompany.ShouldHaveSingleItem().Name.ShouldBe("On Both");
    }

    [Fact]
    public async Task Does_not_list_another_users_contacts()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirApp = await SeedApplicationAsync(theirs.AccessToken);
        await _client.CreateContactAsync(theirs.AccessToken, new { applicationId = theirApp, name = "Theirs" });

        var contacts = await (await _client.ListContactsAsync(mine.AccessToken)).ReadContactListAsync();

        contacts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replaces_the_editable_fields()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var (appId, companyId) = await SeedApplicationWithCompanyAsync(tokens.AccessToken);
        var created = await (await _client.CreateContactAsync(
            tokens.AccessToken, new { applicationId = appId, name = "Dana", notes = "first note" })).ReadContactAsync();

        var updated = await (await _client.UpdateContactAsync(tokens.AccessToken, created.Id, new
        {
            companyId, // move the link from the application to the company
            name = "Dana Prospect",
            role = "referral",
        })).ReadContactAsync();

        updated.ApplicationId.ShouldBeNull();
        updated.CompanyId.ShouldBe(companyId);
        updated.Name.ShouldBe("Dana Prospect");
        updated.Role.ShouldBe("Referral");
        updated.Notes.ShouldBeNull(); // left off the replace, so cleared
        updated.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Returns_404_for_another_users_contact()
    {
        var owner = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var other = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var appId = await SeedApplicationAsync(owner.AccessToken);
        var contact = await (await _client.CreateContactAsync(
            owner.AccessToken, new { applicationId = appId, name = "Owned" })).ReadContactAsync();

        var getResponse = await _client.GetContactAsync(other.AccessToken, contact.Id);
        var updateResponse = await _client.UpdateContactAsync(
            other.AccessToken, contact.Id, new { applicationId = appId, name = "Hijack" });

        await getResponse.ShouldBeProblemAsync(404, "contact.not_found");
        await updateResponse.ShouldBeProblemAsync(404, "contact.not_found");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.ListContactsAsync(accessToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> SeedApplicationAsync(string? accessToken) =>
        (await (await _client.CreateApplicationAsync(accessToken, new { role = "Engineer" })).ReadApplicationAsync()).Id;

    private async Task<(Guid ApplicationId, Guid CompanyId)> SeedApplicationWithCompanyAsync(string? accessToken)
    {
        var application = await (await _client.CreateApplicationAsync(
            accessToken, new { role = "Engineer", companyName = "Acme Corp" })).ReadApplicationAsync();
        return (application.Id, application.CompanyId!.Value);
    }
}
