using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The company type-ahead - <c>GET /api/v1/companies?query=</c> - over a real
/// database: matches the caller's own companies case-insensitively, never another
/// user's, stays silent below three characters, and demands a token. Companies are
/// seeded straight through the context, since the create-application slice that
/// makes them does not exist yet.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CompanySearchEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Returns_the_callers_matching_companies_ordered_by_name()
    {
        var tokens = await _client.RegisterNewUserAsync();
        await SeedCompaniesAsync(UserId.From(tokens.UserId), "Acme Industries", "Acme Corp", "Globex");

        var response = await _client.SearchCompaniesAsync(tokens.AccessToken, "acme");
        var companies = await response.ReadCompaniesAsync();

        // Case-insensitive substring match; Globex is excluded, and the two Acmes
        // come back ordered by name.
        companies.Select(c => c.Name).ShouldBe(["Acme Corp", "Acme Industries"]);
    }

    [Fact]
    public async Task Does_not_return_another_users_companies()
    {
        var mine = await _client.RegisterNewUserAsync();
        var theirs = await _client.RegisterNewUserAsync();
        await SeedCompaniesAsync(UserId.From(theirs.UserId), "Acme Corp");

        var response = await _client.SearchCompaniesAsync(mine.AccessToken, "acme");

        (await response.ReadCompaniesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_nothing_below_the_three_character_minimum()
    {
        var tokens = await _client.RegisterNewUserAsync();
        await SeedCompaniesAsync(UserId.From(tokens.UserId), "Acme Corp");

        var response = await _client.SearchCompaniesAsync(tokens.AccessToken, "ac");

        (await response.ReadCompaniesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_empty_when_nothing_matches()
    {
        var tokens = await _client.RegisterNewUserAsync();
        await SeedCompaniesAsync(UserId.From(tokens.UserId), "Acme Corp");

        var response = await _client.SearchCompaniesAsync(tokens.AccessToken, "zzz");

        (await response.ReadCompaniesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.SearchCompaniesAsync(accessToken: null, "acme");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task SeedCompaniesAsync(UserId ownerId, params string[] names)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        foreach (var name in names)
        {
            db.Companies.Add(new Company { OwnerId = ownerId, Name = name });
        }

        await db.SaveChangesAsync(Ct);
    }
}
