using Jobspect.Modules.Notifications.Domain;

namespace Jobspect.Modules.Notifications.Features;

/// <summary>
/// The automation an account has configured, as it reads back.
/// <para>
/// The id is carried even though no route takes one. The rule is addressed as a
/// singleton - an account has one or has none - but a raised follow-up points back
/// at the rule that raised it, and a client showing that link needs something to
/// match on. It is also what stops the day the cap is lifted from being a change of
/// representation as well as a change of route.
/// </para>
/// </summary>
/// <param name="Id">The rule itself, which a raised follow-up refers to.</param>
/// <param name="DaysAfterApplied">
/// How long an application may sit in Applied before the nudge. Counted from the
/// date the owner says they applied, which is their own truth about when the wait
/// started rather than when they got round to recording it.
/// </param>
/// <param name="CreatedAt">When the account first turned the automation on.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record ReminderRuleResponse(
    Guid Id,
    int DaysAfterApplied,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal static class ReminderRuleResponseMapping
{
    public static ReminderRuleResponse ToResponse(this ReminderRule rule) =>
        new(rule.Id, rule.DaysAfterApplied, rule.CreatedAt, rule.UpdatedAt);
}
