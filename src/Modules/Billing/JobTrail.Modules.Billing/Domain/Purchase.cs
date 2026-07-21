using JobTrail.SharedKernel;

namespace JobTrail.Modules.Billing.Domain;

/// <summary>
/// The record of one completed purchase - the durable evidence a user paid for
/// the Pro unlock, kept even after the plan itself is read. <see cref="ProviderReference"/>
/// is the id the (mocked) billing provider returns, so a purchase can be traced
/// back to the transaction that produced it.
/// </summary>
internal sealed class Purchase
{
    public Guid Id { get; set; }

    public UserId UserId { get; set; }

    /// <summary>The billing provider's own reference for the transaction.</summary>
    public required string ProviderReference { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
