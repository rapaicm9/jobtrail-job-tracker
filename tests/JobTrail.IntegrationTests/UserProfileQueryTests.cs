using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The one sanctioned cross-boundary read of a user's profile, against the real
/// database. Resolved from a live scope rather than driven over HTTP - the query
/// is a Contracts service for other modules, not an endpoint.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UserProfileQueryTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task It_returns_the_users_timezone()
    {
        var tokens = await (await _client.RegisterAsync(ApiClient.UniqueEmail(), timeZoneId: "Europe/Belgrade"))
            .ReadTokensAsync();

        using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IUserProfileQuery>();

        var timezone = await query.GetTimezoneAsync(
            UserId.From(tokens.UserId), TestContext.Current.CancellationToken);

        timezone.ShouldBe("Europe/Belgrade");
    }

    [Fact]
    public async Task It_returns_null_for_an_unknown_user()
    {
        using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IUserProfileQuery>();

        var timezone = await query.GetTimezoneAsync(UserId.New(), TestContext.Current.CancellationToken);

        timezone.ShouldBeNull();
    }
}
