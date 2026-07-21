using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Domain;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The developer grant-Pro shortcut: it unlocks the caller's plan in Development,
/// demands authentication, and - the property that matters - does not exist in
/// production. Each test runs its own host pinned to an environment, against the
/// fixture's shared containers.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GrantProEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Granting_pro_in_development_unlocks_the_plan()
    {
        using var host = HostForEnvironment("Development");
        using var client = host.CreateClient();

        var tokens = await client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // The Free plan is provisioned asynchronously; grant needs it in place.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(userId, Ct) is not null,
            "registration should provision the plan the grant upgrades",
            Ct);

        var response = await client.GrantProAsync(tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.PlanForAsync(userId, Ct)).ShouldNotBeNull().Tier.ShouldBe(PlanTier.Pro);
    }

    [Fact]
    public async Task Granting_pro_demands_authentication()
    {
        using var host = HostForEnvironment("Development");
        using var client = host.CreateClient();

        (await client.GrantProAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_grant_endpoint_does_not_exist_in_production()
    {
        using var host = HostForEnvironment("Production");
        using var client = host.CreateClient();

        // Authenticated, so a 404 can only mean the route was never mapped.
        var tokens = await client.RegisterNewUserAsync();

        (await client.GrantProAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private JobTrailApiFactory HostForEnvironment(string environment)
    {
        var settings = fixture.BuildSettings();
        settings["environment"] = environment;
        return new JobTrailApiFactory(settings);
    }
}
