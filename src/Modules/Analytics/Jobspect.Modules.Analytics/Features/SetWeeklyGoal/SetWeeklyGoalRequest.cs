namespace Jobspect.Modules.Analytics.Features.SetWeeklyGoal;

/// <summary>
/// The target to aim at this week and every week after it. Nullable so an omitted
/// field is a validation failure rather than a silent zero - the client has to say
/// what it means, and "no goal" is said by deleting the goal, not by sending one of
/// nothing.
/// </summary>
internal sealed record SetWeeklyGoalRequest(int? Target);
