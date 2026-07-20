using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The 429 behavior deferred from the rate-limiting slice. Runs its own host
/// with a two-permit auth budget against the fixture's containers; in-process
/// requests share one partition (no remote address), which is exactly what a
/// throttling test wants.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitingTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_auth_window_throttles_with_a_problem_and_retry_after()
    {
        var settings = fixture.BuildSettings();
        settings["RateLimiting:AuthPermitLimit"] = "2";

        using var throttled = new JobTrailApiFactory(settings);
        using var client = throttled.CreateClient();

        // Two permits spent - failed logins count like any other request.
        (await client.LoginAsync(ApiClient.UniqueEmail())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.LoginAsync(ApiClient.UniqueEmail())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var third = await client.LoginAsync(ApiClient.UniqueEmail());

        await third.ShouldBeProblemAsync(429);
        third.Headers.TryGetValues("Retry-After", out var retryAfter).ShouldBeTrue();
        int.Parse(retryAfter!.Single()).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_auth_window_does_not_constrain_the_rest_of_the_api()
    {
        var settings = fixture.BuildSettings();
        settings["RateLimiting:AuthPermitLimit"] = "2";

        using var throttled = new JobTrailApiFactory(settings);
        using var client = throttled.CreateClient();

        await client.LoginAsync(ApiClient.UniqueEmail());
        await client.LoginAsync(ApiClient.UniqueEmail());
        (await client.LoginAsync(ApiClient.UniqueEmail())).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Health lives outside the limited groups and stays reachable.
        var health = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
