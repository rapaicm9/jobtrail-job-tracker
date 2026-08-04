using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Features.GetReminderRule;

/// <summary>
/// The caller's follow-up automation, if they have configured one.
/// <para>
/// Ownership is inside the query rather than checked beside it - the resource is
/// addressed by who is asking, so there is nothing else it could return.
/// </para>
/// </summary>
internal sealed class GetReminderRuleHandler(NotificationsDbContext dbContext)
{
    public async Task<Result<ReminderRuleResponse>> HandleAsync(
        UserId ownerId, CancellationToken cancellationToken)
    {
        var rule = await dbContext.ReminderRules
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.OwnerId == ownerId, cancellationToken);

        return rule is null ? ReminderRuleErrors.NotFound : rule.ToResponse();
    }
}
