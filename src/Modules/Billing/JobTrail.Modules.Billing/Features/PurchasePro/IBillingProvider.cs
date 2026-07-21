using JobTrail.SharedKernel;

namespace JobTrail.Modules.Billing.Features.PurchasePro;

/// <summary>
/// The payment gateway, behind a seam so the real one is swapped in without the
/// purchase flow changing. The provider knows nothing of plans or tiers - it
/// charges and reports the outcome; Billing decides what a successful charge
/// means for entitlement.
/// </summary>
internal interface IBillingProvider
{
    Task<PaymentResult> ChargeAsync(UserId userId, CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of a charge. <see cref="Reference"/> is the provider's own id for
/// the transaction, kept on the purchase record; empty when the charge failed.
/// </summary>
internal sealed record PaymentResult(bool Succeeded, string Reference);
