using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Billing.Domain;
using JobTrail.Modules.Billing.Features.PurchasePro;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The one-time Pro unlock against the real store: a charge flips the plan,
/// records the purchase and announces the change; a repeat never charges twice;
/// a declined payment leaves no trace.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PurchaseProTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Purchasing_flips_the_plan_records_it_and_announces_it()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Free, Ct);
        var bus = new RecordingEventBus();
        var provider = new StubBillingProvider(succeeds: true);

        var result = await PurchaseAsync(userId, provider, bus);

        result.IsSuccess.ShouldBeTrue();
        provider.Charges.ShouldBe(1);

        var plan = (await fixture.PlanForAsync(userId, Ct)).ShouldNotBeNull();
        plan.Tier.ShouldBe(PlanTier.Pro);
        plan.UpdatedAt.ShouldNotBeNull();
        (await fixture.PurchaseCountAsync(userId, Ct)).ShouldBe(1);

        bus.Published.ShouldHaveSingleItem().ShouldBeOfType<EntitlementChanged>().UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Purchasing_when_already_pro_never_charges_again()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Pro, Ct);
        var bus = new RecordingEventBus();
        var provider = new StubBillingProvider(succeeds: true);

        var result = await PurchaseAsync(userId, provider, bus);

        result.IsSuccess.ShouldBeTrue();
        provider.Charges.ShouldBe(0);
        bus.Published.ShouldBeEmpty();
        (await fixture.PurchaseCountAsync(userId, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_declined_payment_leaves_the_plan_free()
    {
        var userId = UserId.New();
        await fixture.SeedPlanAsync(userId, PlanTier.Free, Ct);
        var bus = new RecordingEventBus();
        var provider = new StubBillingProvider(succeeds: false);

        var result = await PurchaseAsync(userId, provider, bus);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("billing.payment_failed");

        (await fixture.PlanForAsync(userId, Ct)).ShouldNotBeNull().Tier.ShouldBe(PlanTier.Free);
        (await fixture.PurchaseCountAsync(userId, Ct)).ShouldBe(0);
        bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Purchasing_with_no_plan_is_a_not_found()
    {
        var result = await PurchaseAsync(UserId.New(), new StubBillingProvider(succeeds: true), new RecordingEventBus());

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("billing.plan_not_found");
    }

    [Fact]
    public async Task The_composed_handler_unlocks_pro_through_the_mock_provider()
    {
        // A real registered user, whose Free plan is provisioned asynchronously.
        var tokens = await _client.RegisterNewUserAsync();
        var userId = UserId.From(tokens.UserId);
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(userId, Ct) is not null,
            "registration should provision the plan the purchase upgrades",
            Ct);

        using (var scope = fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<PurchaseProHandler>();
            (await handler.HandleAsync(userId, Ct)).IsSuccess.ShouldBeTrue();
        }

        (await fixture.PlanForAsync(userId, Ct)).ShouldNotBeNull().Tier.ShouldBe(PlanTier.Pro);
    }

    private async Task<Result> PurchaseAsync(UserId userId, IBillingProvider provider, RecordingEventBus bus)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var handler = new PurchaseProHandler(db, provider, bus, TimeProvider.System);
        return await handler.HandleAsync(userId, Ct);
    }

    /// <summary>A payment gateway a test can steer, and that counts its charges.</summary>
    private sealed class StubBillingProvider(bool succeeds) : IBillingProvider
    {
        public int Charges { get; private set; }

        public Task<PaymentResult> ChargeAsync(UserId userId, CancellationToken cancellationToken)
        {
            Charges++;
            return Task.FromResult(new PaymentResult(succeeds, succeeds ? "stub_ref" : string.Empty));
        }
    }
}
