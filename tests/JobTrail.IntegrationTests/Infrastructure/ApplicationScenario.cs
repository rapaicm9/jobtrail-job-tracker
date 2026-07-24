using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Sets up the state the application-write tests start from. The default campaign
/// an application needs is provisioned asynchronously off <c>UserRegistered</c>,
/// so a freshly-registered user has no campaign for a beat; these helpers wait for
/// it before handing the tokens back, so a create never races the provisioning.
/// </summary>
internal static class ApplicationScenario
{
    public static async Task<AuthTokens> RegisterWithDefaultCampaignAsync(
        this ApiFixture fixture, HttpClient client, CancellationToken cancellationToken, string? timeZoneId = null)
    {
        var response = await client.RegisterAsync(ApiClient.UniqueEmail(), timeZoneId: timeZoneId);
        var tokens = await response.ReadTokensAsync();
        var ownerId = UserId.From(tokens.UserId);

        await Poll.UntilAsync(
            () => fixture.HasDefaultCampaignAsync(ownerId, cancellationToken),
            "registration should provision the default campaign before an application is created",
            cancellationToken);

        return tokens;
    }

    public static async Task<Guid> DefaultCampaignIdAsync(
        this ApiFixture fixture, UserId ownerId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        return await db.Campaigns
            .Where(c => c.OwnerId == ownerId && c.IsDefault)
            .Select(c => c.Id)
            .SingleAsync(cancellationToken);
    }

    private static async Task<bool> HasDefaultCampaignAsync(
        this ApiFixture fixture, UserId ownerId, CancellationToken cancellationToken)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        return await db.Campaigns.AnyAsync(c => c.OwnerId == ownerId && c.IsDefault, cancellationToken);
    }
}
