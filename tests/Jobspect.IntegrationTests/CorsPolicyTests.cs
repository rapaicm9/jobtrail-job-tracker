using Jobspect.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// What the CORS policy grants a browser, pinned because narrowing it was a
/// decision rather than an omission.
/// <para>
/// No browser is a client of this API: the web client's tokens stay in Next.js
/// route handlers, so the calls arrive from Node, which never preflights. The
/// allowlist therefore carries what an anonymous JSON request needs and nothing
/// more - in particular not <c>Authorization</c>, which would describe a browser
/// holding a bearer token, the one client the token model rules out.
/// </para>
/// <para>
/// Both tests run a host with the origin configured here rather than inheriting one
/// from the environment's settings. Without that, an ungranted preflight would be
/// ungranted because no origin was ever allowed, and the assertions below would
/// hold whatever the header allowlist said.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CorsPolicyTests(ApiFixture fixture)
{
    private const string Origin = "https://client.example";

    [Fact]
    public async Task A_preflight_for_a_json_body_is_granted()
    {
        using var host = HostAllowingTheOrigin();
        using var client = host.CreateClient();

        var response = await PreflightAsync(client, requestedHeaders: "content-type");

        // This is what makes the next test mean anything: the origin really is
        // allowed and a JSON body really is granted, so what that one withholds is
        // the header rather than the caller.
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Origin]);
        response.Headers.GetValues("Access-Control-Allow-Headers").ShouldBe(["Content-Type"]);
    }

    [Fact]
    public async Task A_browser_is_never_granted_the_authorization_header()
    {
        using var host = HostAllowingTheOrigin();
        using var client = host.CreateClient();

        var response = await PreflightAsync(client, requestedHeaders: "authorization");

        // The preflight is answered; it is the grant that refuses. The browser asked
        // whether it may send Authorization, the answer names only what the policy
        // allows, and the browser compares the two and declines to make the call.
        //
        // Asserted as the exact set rather than the absence of one header, so a
        // future addition has to be deliberate: whatever is granted here is the whole
        // of what a page may send this API.
        response.Headers.GetValues("Access-Control-Allow-Headers").ShouldBe(["Content-Type"]);
    }

    private JobspectApiFactory HostAllowingTheOrigin()
    {
        var settings = fixture.BuildSettings();
        settings["Cors:AllowedOrigins:0"] = Origin;

        return new JobspectApiFactory(settings);
    }

    private static Task<HttpResponseMessage> PreflightAsync(HttpClient client, string requestedHeaders)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/applications");
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", requestedHeaders);

        return client.SendAsync(request);
    }
}
