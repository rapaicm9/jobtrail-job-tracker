using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Features.EraseData;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Billing's slice of the erasure fan-out: an erasure request takes the user's
/// plan and every purchase behind it out of the store, is a no-op when there is
/// nothing to erase, and actually reaches Billing when an account is deleted.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BillingErasureTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Erasure_removes_the_users_plan_and_purchases()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Pro, Ct);
        await fixture.SeedPurchaseAsync(userId, Ct);

        await EraseAsync(userId);

        (await fixture.PlanForAsync(userId, Ct)).ShouldBeNull();
        (await fixture.PurchaseCountAsync(userId, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Erasing_a_user_with_no_billing_data_does_nothing()
    {
        // Idempotent: the at-least-once bus can deliver to a user already erased,
        // or one Billing never held data for.
        await Should.NotThrowAsync(EraseAsync(UserId.New()));
    }

    [Fact]
    public async Task Deleting_the_account_erases_the_users_billing_data()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);

        // The Free plan is provisioned asynchronously; wait for it before erasing,
        // so a passing test proves removal, not a race against provisioning.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(userId, Ct) is not null,
            "registration should provision the plan the erasure removes",
            Ct);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The deletion fans out through the bus and reaches Billing shortly after.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(userId, Ct) is null,
            "account erasure should remove the user's billing plan",
            Ct);
    }

    private async Task EraseAsync(UserId userId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var handler = new BillingDataErasureHandler(db);
        await handler.HandleAsync(new UserDataDeletionRequested(userId), Ct);
    }
}
