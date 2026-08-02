using System.Text.Json;
using System.Text.Json.Nodes;
using Jobspect.Modules.Billing.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Billing.Features.ExportData;

/// <summary>
/// Billing's contribution to an account export: what the account is entitled to,
/// and the purchases that entitled it.
/// <para>
/// It belongs in an export even though the user never typed it. A purchase is a
/// record of something they did and paid for, and it is the one part of their
/// account they may later need to show someone else.
/// </para>
/// <para>
/// The provider's own transaction reference travels with each purchase - it is
/// what makes a line in this file traceable back to a real transaction, and with
/// the payment provider mocked it is the only evidence there is.
/// </para>
/// </summary>
internal sealed class BillingDataExporter(BillingDbContext dbContext) : IUserDataExporter
{
    public string Section => "billing";

    public async Task<JsonNode> ExportAsync(UserId userId, CancellationToken cancellationToken)
    {
        var plan = await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new PlanExport(p.Tier.ToString(), p.CreatedAt, p.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var purchases = await dbContext.Purchases
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Select(p => new PurchaseExport(p.ProviderReference, p.CreatedAt))
            .ToListAsync(cancellationToken);

        return JsonSerializer.SerializeToNode(new BillingExport(plan, purchases), ExportJson.Options)
            ?? new JsonObject();
    }

    private sealed record BillingExport(PlanExport? Plan, IReadOnlyList<PurchaseExport> Purchases);

    private sealed record PlanExport(string Tier, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record PurchaseExport(string ProviderReference, DateTimeOffset CreatedAt);
}
