using System.Text.Json;
using System.Text.Json.Nodes;
using Jobspect.Modules.Analytics.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Analytics.Features.ExportData;

/// <summary>
/// This module's contribution to an account export: the weekly goal, and nothing
/// else.
/// <para>
/// What is absent is almost everything, and that is the point. The base rows here
/// were assembled from events another module published - every figure the dashboard
/// shows is something the account already told us in a different shape, and an
/// export of derived data hands a person their own record back twice. The goal is
/// the one exception in this schema: no event carries it, nothing can rebuild it,
/// and it is a number the account typed. Losing it loses it, which is precisely the
/// property that makes an export worth having.
/// </para>
/// <para>
/// A section is written even when there is no goal. An absent section and an empty
/// one say the same thing to a reader, and only one of them says it without leaving
/// them to wonder whether the module simply failed.
/// </para>
/// </summary>
internal sealed class AnalyticsDataExporter(AnalyticsDbContext dbContext) : IUserDataExporter
{
    public string Section => "analytics";

    public async Task<JsonNode> ExportAsync(UserId userId, CancellationToken cancellationToken)
    {
        // Projected, not loaded: the columns named here are the only ones that can
        // reach the document, so a column added to the row later cannot arrive in an
        // export by accident.
        var goal = await dbContext.WeeklyGoals
            .AsNoTracking()
            .Where(weeklyGoal => weeklyGoal.OwnerId == userId)
            .Select(weeklyGoal => new WeeklyGoalExport(
                weeklyGoal.Target, weeklyGoal.CreatedAt, weeklyGoal.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return JsonSerializer.SerializeToNode(new AnalyticsExport(goal), ExportJson.Options) ?? new JsonObject();
    }

    private sealed record AnalyticsExport(WeeklyGoalExport? WeeklyGoal);

    private sealed record WeeklyGoalExport(int Target, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
