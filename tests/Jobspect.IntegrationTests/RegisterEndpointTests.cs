using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RegisterEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task The_new_account_is_announced_in_the_same_write_that_opens_it()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // No polling: the account exists by the time the response is in hand, and
        // so must its announcement. Everything the account needs to function -
        // its plan, its campaign - is stood up from this row by modules that
        // cannot read Identity's tables to notice an account they were never told
        // about. Recorded a moment later instead, a crash in between would leave
        // an account that can never file an application.
        var announced = (await fixture.RegistrationAnnouncementsForAsync(userId, Ct)).ShouldHaveSingleItem();
        announced.Payload.ShouldContain(tokens.UserId.ToString());
    }

    [Fact]
    public async Task A_rejected_registration_announces_nothing()
    {
        var email = ApiClient.UniqueEmail();
        var first = await (await _client.RegisterAsync(email)).ReadTokensAsync();

        await (await _client.RegisterAsync(email)).ShouldBeProblemAsync(409, "registration.email_taken");

        // One account, one announcement. The rejected attempt records nothing -
        // and, since the announcement is written before the account it describes,
        // this is what proves the two ride the same save rather than the row being
        // committed on its own and orphaned.
        (await fixture.RegistrationAnnouncementsForAsync(UserId.From(first.UserId), Ct)).ShouldHaveSingleItem();
    }
}
