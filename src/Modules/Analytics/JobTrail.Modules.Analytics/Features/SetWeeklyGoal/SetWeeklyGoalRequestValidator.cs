namespace JobTrail.Modules.Analytics.Features.SetWeeklyGoal;

/// <summary>Shape-level checks on a weekly-goal request, keyed by field.</summary>
internal static class SetWeeklyGoalRequestValidator
{
    public const int MinTarget = 1;

    /// <summary>
    /// A ceiling rather than a meaningful limit. Nobody sends four hundred
    /// applications in a week, and a bound keeps an absurd number out of a column
    /// that later arithmetic divides by.
    /// </summary>
    public const int MaxTarget = 100;

    public static Dictionary<string, string[]>? Validate(SetWeeklyGoalRequest request)
    {
        var errors = new ValidationErrors();

        if (request.Target is not { } target)
        {
            errors.Add("target", "A weekly target is required.");
        }
        else if (target is < MinTarget or > MaxTarget)
        {
            // Zero is refused with the rest, and the message says where to go
            // instead: tracking no goal is the absence of the goal, so there is
            // exactly one way to express it and it is not a number.
            errors.Add(
                "target",
                $"The weekly target must be between {MinTarget} and {MaxTarget}. "
                    + "Delete the goal to stop tracking one.");
        }

        return errors.ToResultOrNull();
    }
}
