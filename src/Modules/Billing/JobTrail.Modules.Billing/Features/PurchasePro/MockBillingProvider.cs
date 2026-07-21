using JobTrail.SharedKernel;

namespace JobTrail.Modules.Billing.Features.PurchasePro;

/// <summary>
/// The stand-in payment gateway for v1: every charge succeeds and returns a
/// synthetic reference. It exists so the whole purchase-and-entitlement flow is
/// real and testable end to end while the product carries no real payments; a
/// live provider replaces it behind <see cref="IBillingProvider"/> with nothing
/// else changing.
/// </summary>
internal sealed class MockBillingProvider : IBillingProvider
{
    public Task<PaymentResult> ChargeAsync(UserId userId, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentResult(Succeeded: true, Reference: $"mock_{Guid.CreateVersion7():N}"));
}
