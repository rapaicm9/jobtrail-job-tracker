using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobTrail.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RegisterEndpointTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Registering_returns_201_and_a_full_token_pair()
    {
        var response = await _client.RegisterAsync(ApiClient.UniqueEmail(), timeZoneId: "Europe/Belgrade");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tokens = await response.ReadTokensAsync();

        tokens.UserId.ShouldNotBe(Guid.Empty);
        tokens.AccessToken.ShouldNotBeNullOrEmpty();
        tokens.RefreshToken.ShouldNotBeNullOrEmpty();
        tokens.AccessTokenExpiresAt.ShouldBeLessThan(tokens.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Registering_a_taken_email_returns_a_409_problem()
    {
        var email = ApiClient.UniqueEmail();
        await _client.RegisterAsync(email);

        var second = await _client.RegisterAsync(email);

        await second.ShouldBeProblemAsync(409, "registration.email_taken");
    }

    [Fact]
    public async Task A_malformed_request_returns_a_field_keyed_422()
    {
        var response = await _client.RegisterAsync("not-an-email", password: "short");

        var problem = await response.ShouldBeValidationProblemAsync("email", "password");

        // Every unmet password rule arrives in one round trip.
        problem.Errors["password"].Length.ShouldBe(4);
    }
}
