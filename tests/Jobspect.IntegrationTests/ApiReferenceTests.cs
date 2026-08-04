using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The API reference page, and the content-security policy it is the sole
/// exception to. The exception is the part worth pinning: a policy that widened
/// for every response would be a real loosening of a JSON API's posture, and it
/// would loosen quietly.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApiReferenceTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_reference_renders_and_points_at_the_served_document()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/scalar/", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var page = await response.Content.ReadAsStringAsync(Ct);

        // Relative, and resolved by the bundle against the origin rather than
        // against /scalar/ - so this is the document the browser fetches.
        page.ShouldContain("openapi/v1.json");

        // The document declares one scheme; the page should open on it.
        page.ShouldContain("bearer");
    }

    [Fact]
    public async Task The_widened_policy_applies_to_the_reference_alone()
    {
        var client = fixture.CreateClient();

        var reference = await client.GetAsync("/scalar/", Ct);
        var policy = reference.Headers.GetValues("Content-Security-Policy").Single();

        policy.ShouldContain("script-src 'self' 'unsafe-inline'");
        policy.ShouldContain("connect-src 'self'");

        // Widened, not opened: no default source, and nothing may frame it.
        policy.ShouldContain("default-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        reference.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);

        // Nothing off this origin, which is what turning the webfonts off buys.
        policy.ShouldNotContain("http://");
        policy.ShouldNotContain("https://");
    }

    [Fact]
    public async Task An_api_route_keeps_the_json_policy()
    {
        var client = fixture.CreateClient();

        var api = await client.GetAsync("/api/v1/account", Ct);

        api.Headers.GetValues("Content-Security-Policy")
            .ShouldBe(["default-src 'none'; frame-ancestors 'none'"]);
    }
}
