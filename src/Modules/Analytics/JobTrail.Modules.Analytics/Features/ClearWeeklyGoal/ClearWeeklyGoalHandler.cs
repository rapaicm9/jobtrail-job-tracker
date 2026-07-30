using JobTrail.Modules.Analytics.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Features.ClearWeeklyGoal;

/// <summary>
/// Stops the account tracking a weekly goal by removing the row that held it.
/// <para>
/// Deleting the row rather than zeroing a column, so "no goal" has one
/// representation and not two - a zero stored in the table would be a target the
/// account can never meet, and every read would have to know to treat it as absent.
/// </para>
/// </summary>
internal sealed class ClearWeeklyGoalHandler(AnalyticsDbContext dbContext)
{
    /// <summary>
    /// Removes the caller's goal if they have one. Deleting a goal that is not
    /// there is success, not a 404: the caller asked to be left without one and
    /// they are, and a client retrying a dropped response should not be told off
    /// for it.
    /// </summary>
    public Task ClearAsync(UserId ownerId, CancellationToken cancellationToken) =>
        dbContext.WeeklyGoals
            .Where(goal => goal.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
}
