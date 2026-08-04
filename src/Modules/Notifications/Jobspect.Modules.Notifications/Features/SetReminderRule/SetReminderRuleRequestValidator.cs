namespace Jobspect.Modules.Notifications.Features.SetReminderRule;

/// <summary>Shape-level checks on a follow-up rule request, keyed by field.</summary>
internal static class SetReminderRuleRequestValidator
{
    /// <summary>
    /// What the automation starts at when an account first turns it on. Not applied
    /// here - the request carries its own number and says so - but held here so the
    /// figure has one home, for the client that pre-fills the form and the API
    /// description generated from it.
    /// </summary>
    public const int DefaultDaysAfterApplied = 7;

    /// <summary>
    /// A day is the shortest wait that means anything. Zero would raise the nudge
    /// the moment the application was recorded, which is not a follow-up.
    /// </summary>
    public const int MinDaysAfterApplied = 1;

    /// <summary>
    /// A quarter of a year, and a ceiling rather than a considered limit. Past this
    /// the reminder arrives long after the owner has stopped thinking about the
    /// application, and a bound keeps an absurd number out of a column that date
    /// arithmetic is done against.
    /// </summary>
    public const int MaxDaysAfterApplied = 90;

    public static Dictionary<string, string[]>? Validate(SetReminderRuleRequest request)
    {
        var errors = new ValidationErrors();

        if (request.DaysAfterApplied is not { } days)
        {
            errors.Add(
                "daysAfterApplied",
                $"A number of days is required. The usual choice is {DefaultDaysAfterApplied}.");
        }
        else if (days is < MinDaysAfterApplied or > MaxDaysAfterApplied)
        {
            // The message names the way out, as the weekly goal's does: turning the
            // automation off is the absence of the rule, not a number standing for
            // it, so there is exactly one way to say it.
            errors.Add(
                "daysAfterApplied",
                $"The number of days must be between {MinDaysAfterApplied} and {MaxDaysAfterApplied}. "
                    + "Delete the rule to stop following up automatically.");
        }

        return errors.ToResultOrNull();
    }
}
