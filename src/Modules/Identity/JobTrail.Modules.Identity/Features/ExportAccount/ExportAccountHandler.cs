using System.Text.Json;
using System.Text.Json.Nodes;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Features.ExportAccount;

/// <summary>
/// Gathers everything the system holds for one account into a single document.
/// <para>
/// The read-side twin of the erasure fan-out, and it sits here for the same reason
/// erasure does: an account-wide operation belongs with the account. What it does
/// <em>not</em> do is know which modules exist. Each one registers an
/// <see cref="IUserDataExporter"/> and this asks all of them, so Identity composes
/// an export of the Applications module's data without a reference to it, and a
/// module that gains user data later joins by registering an exporter rather than
/// by editing this.
/// </para>
/// <para>
/// Sections are sorted by name before assembly. They arrive in dependency-injection
/// order, which is the order the host happens to register modules in - a document
/// whose shape depends on that would be a document that changes for no reason.
/// </para>
/// </summary>
internal sealed class ExportAccountHandler(
    IEnumerable<IUserDataExporter> exporters,
    IEntitlementQuery entitlements,
    TimeProvider timeProvider)
{
    public async Task<Result<byte[]>> HandleAsync(UserId userId, CancellationToken cancellationToken)
    {
        // The route policy has already refused an unentitled caller, so this never
        // fires through the endpoint. It fires if the endpoint is ever mapped
        // without its policy, or if this handler is called from somewhere that is
        // not that endpoint - the gate belongs to the operation, not to one route.
        // Checked before anything is gathered: an export nobody may have is not
        // worth the reads.
        if (!await entitlements.HasEntitlementAsync(userId, Entitlement.Export, cancellationToken))
        {
            return AccountErrors.ExportNotEntitled;
        }

        var document = new JsonObject
        {
            // Enough for a reader to know what they are holding a year later,
            // without having to guess which account or which day it came from.
            ["exportedAt"] = JsonValue.Create(timeProvider.GetUtcNow()),
            ["accountId"] = JsonValue.Create(userId.Value),
        };

        // Sequentially, not in parallel: each exporter resolves its own scoped
        // DbContext from the same request scope, and a DbContext serves one
        // operation at a time.
        foreach (var exporter in exporters.OrderBy(exporter => exporter.Section, StringComparer.Ordinal))
        {
            document[exporter.Section] = await exporter.ExportAsync(userId, cancellationToken);
        }

        // Indented, because the one thing certain about this file is that a person
        // will open it. Written as UTF-8 bytes here rather than streamed to the
        // response: the whole document has to succeed or fail before a status code
        // is chosen, and at one account's size there is nothing to gain by
        // committing to a 200 before the last module has answered.
        return JsonSerializer.SerializeToUtf8Bytes(document, ExportDocumentJson);
    }

    private static readonly JsonSerializerOptions ExportDocumentJson = new() { WriteIndented = true };
}
