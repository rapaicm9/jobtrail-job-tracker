using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Features;

/// <summary>
/// Reads an account's goal and this week's progress toward it. Shared by the slice
/// that returns it and the slice that sets it, so a write answers with exactly what
/// a following read would say and a client needs no second call.
/// </summary>
internal sealed class WeeklyGoalReader(
    AnalyticsDbContext dbContext, IUserProfileQuery profileQuery, TimeProvider timeProvider)
{
    public async Task<WeeklyGoalResponse> ReadAsync(UserId ownerId, CancellationToken cancellationToken)
    {
        // Which week it is depends on where the caller is: at any moment there are
        // two dates in the world, and near a Sunday midnight two weeks. The same
        // resolution the Applications module stamps an applied date with, so an
        // application opened now always lands in the week this call reports.
        var timezoneId = await profileQuery.GetTimezoneAsync(ownerId, cancellationToken);
        var weekStart = IsoWeek.WeekStarting(LocalDate.TodayIn(timeProvider.GetUtcNow(), timezoneId));

        var target = await dbContext.WeeklyGoals
            .AsNoTracking()
            .Where(goal => goal.OwnerId == ownerId)
            .Select(goal => (int?)goal.Target)
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
        {
            // No goal, so no progress - see WeeklyGoalResponse.Applied. The week is
            // still reported: a client showing "set a goal" wants to say which week
            // it would be for.
            return new WeeklyGoalResponse(null, null, weekStart);
        }

        var weekEnd = weekStart.AddDays(6);

        // Counted on the date the user says they applied, not on when the row got
        // here. An application entered today but backdated to last week belongs to
        // last week's effort, and one entered late still counts for the week it was
        // actually sent. A row whose applied date has not arrived yet cannot be
        // placed on the calendar at all, and a null fails both comparisons, so it
        // is left out rather than guessed at - as the weekly trend leaves it out.
        var applied = await dbContext.ApplicationFacts
            .AsNoTracking()
            .CountAsync(
                facts => facts.OwnerId == ownerId
                    && facts.AppliedDate >= weekStart
                    && facts.AppliedDate <= weekEnd,
                cancellationToken);

        return new WeeklyGoalResponse(target, applied, weekStart);
    }
}
