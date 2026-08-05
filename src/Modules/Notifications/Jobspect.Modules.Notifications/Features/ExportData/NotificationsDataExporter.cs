using System.Text.Json;
using System.Text.Json.Nodes;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.ExportData;

/// <summary>
/// This module's contribution to an account export: the follow-up automation the
/// account set up, and nothing else.
/// <para>
/// The rule is the one thing in this schema the account stated rather than this
/// module derived - a number the user chose, carried by no event and rebuildable
/// from nothing. The other three tables are not the user's data in the sense an
/// export means. The reminders and their deliveries are this module's own record of
/// what it decided to say and when it said it, which is delivery bookkeeping; and
/// the tracked applications are a copy, kept for scheduling, of facts the
/// Applications module already exports in full.
/// </para>
/// <para>
/// A section is written even when there is no rule, for the reason the kernel's
/// contract gives: an absent section and an empty one say the same thing, and only
/// one of them says it unambiguously.
/// </para>
/// </summary>
internal sealed class NotificationsDataExporter(NotificationsDbContext dbContext) : IUserDataExporter
{
    public string Section => "notifications";

    public async Task<JsonNode> ExportAsync(UserId userId, CancellationToken cancellationToken)
    {
        // Projected, not loaded: the columns named here are the only ones that can
        // reach the document, so a column added to the row later cannot arrive in an
        // export by accident.
        var rule = await dbContext.ReminderRules
            .AsNoTracking()
            .Where(reminderRule => reminderRule.OwnerId == userId)
            .Select(reminderRule => new ReminderRuleExport(
                reminderRule.DaysAfterApplied, reminderRule.CreatedAt, reminderRule.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return JsonSerializer.SerializeToNode(new NotificationsExport(rule), ExportJson.Options)
            ?? new JsonObject();
    }

    private sealed record NotificationsExport(ReminderRuleExport? ReminderRule);

    private sealed record ReminderRuleExport(
        int DaysAfterApplied, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
