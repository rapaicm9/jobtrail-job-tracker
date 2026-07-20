using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class LoginEndpointTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Correct_credentials_return_a_token_pair()
    {
        var email = ApiClient.UniqueEmail();
        var registered = await (await _client.RegisterAsync(email)).ReadTokensAsync();

        var response = await _client.LoginAsync(email, deviceLabel: "Firefox on Linux");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await response.ReadTokensAsync();
        tokens.UserId.ShouldBe(registered.UserId);
        tokens.RefreshToken.ShouldNotBe(registered.RefreshToken);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_are_indistinguishable()
    {
        var email = ApiClient.UniqueEmail();
        await _client.RegisterAsync(email);

        var wrongPassword = await _client.LoginAsync(email, password: "Wrong-horse7");
        var unknownEmail = await _client.LoginAsync(ApiClient.UniqueEmail(), password: "Wrong-horse7");

        var first = await wrongPassword.ShouldBeProblemAsync(401, "auth.invalid_credentials");
        var second = await unknownEmail.ShouldBeProblemAsync(401, "auth.invalid_credentials");
        first.Detail.ShouldBe(second.Detail);
    }

    [Fact]
    public async Task Missing_credentials_return_a_field_keyed_422()
    {
        var response = await _client.LoginAsync(email: "", password: "");

        await response.ShouldBeValidationProblemAsync("email", "password");
    }
}
