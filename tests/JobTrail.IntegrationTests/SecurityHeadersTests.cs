using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SecurityHeadersTests(ApiFixture fixture)
{
    [Fact]
    public async Task Every_response_carries_the_hardening_headers()
    {
        var client = fixture.CreateClient();

        var response = await client.LoginAsync(ApiClient.UniqueEmail());

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy")
            .ShouldBe(["default-src 'none'; frame-ancestors 'none'"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
    }
}
