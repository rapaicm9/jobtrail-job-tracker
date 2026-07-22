using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Domain;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The authenticated billing surface: a caller reads their own plan status, and
/// unlocks Pro through the mocked provider. Both demand authentication and act on
/// whoever the token proves, never an id from the request. Runs against the
/// fixture's shared containers.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Reading_the_plan_returns_the_free_tier_a_new_account_starts_on()
    {
        var (tokens, _) = await RegisterWithProvisionedPlanAsync();

        var plan = await (await _client.GetPlanAsync(tokens.AccessToken)).ReadPlanAsync();

        plan.Tier.ShouldBe(nameof(PlanTier.Free));
        plan.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Reading_the_plan_demands_authentication()
    {
        (await _client.GetPlanAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Purchasing_unlocks_pro_and_returns_the_new_status()
    {
        var (tokens, userId) = await RegisterWithProvisionedPlanAsync();

        var purchased = await (await _client.PurchaseProAsync(tokens.AccessToken)).ReadPlanAsync();

        purchased.Tier.ShouldBe(nameof(PlanTier.Pro));
        purchased.UpdatedAt.ShouldNotBeNull();

        // The store reflects the flip, and a follow-up read agrees.
        (await fixture.PlanForAsync(userId, Ct)).ShouldNotBeNull().Tier.ShouldBe(PlanTier.Pro);
        (await (await _client.GetPlanAsync(tokens.AccessToken)).ReadPlanAsync()).Tier.ShouldBe(nameof(PlanTier.Pro));
    }

    [Fact]
    public async Task Purchasing_again_when_already_pro_returns_pro_without_a_second_purchase()
    {
        var (tokens, userId) = await RegisterWithProvisionedPlanAsync();

        (await _client.PurchaseProAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await (await _client.PurchaseProAsync(tokens.AccessToken)).ReadPlanAsync();

        second.Tier.ShouldBe(nameof(PlanTier.Pro));
        (await fixture.PurchaseCountAsync(userId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Purchasing_demands_authentication()
    {
        (await _client.PurchaseProAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Registers a fresh account and waits for the Free plan the endpoints act on -
    /// it is provisioned asynchronously off <c>UserRegistered</c>.
    /// </summary>
    private async Task<(AuthTokens Tokens, UserId UserId)> RegisterWithProvisionedPlanAsync()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(userId, Ct) is not null,
            "registration should provision the plan the billing endpoints read",
            Ct);
        return (tokens, userId);
    }
}
