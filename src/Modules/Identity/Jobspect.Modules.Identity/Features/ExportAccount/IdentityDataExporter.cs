using System.Text.Json;
using System.Text.Json.Nodes;
using Jobspect.Modules.Identity.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Identity.Features.ExportAccount;

/// <summary>
/// Identity's contribution to an account export: the profile the user gave us and
/// nothing else.
/// <para>
/// What is missing is the point. The password hash, the security stamp, the
/// refresh tokens and the token version all live on or beside this row, and none
/// of them is the user's data - they are how the account is secured, and writing
/// them into a file the user downloads (and forwards, and stores) would be handing
/// out the keys along with the contents. The export carries what a person typed:
/// their address, their timezone, and when they joined.
/// </para>
/// </summary>
internal sealed class IdentityDataExporter(IdentityModuleDbContext dbContext) : IUserDataExporter
{
    public string Section => "identity";

    public async Task<JsonNode> ExportAsync(UserId userId, CancellationToken cancellationToken)
    {
        var id = userId.Value;

        // Projected, not loaded: the columns named here are the only ones that can
        // reach the document, so a field added to the user row later cannot arrive
        // in an export by accident.
        var account = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new AccountExport(user.Email, user.TimeZoneId, user.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return JsonSerializer.SerializeToNode(account, ExportJson.Options) ?? new JsonObject();
    }

    private sealed record AccountExport(string? Email, string TimeZoneId, DateTimeOffset CreatedAt);
}
