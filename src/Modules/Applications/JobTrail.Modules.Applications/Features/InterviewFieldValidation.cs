using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The shape rules an interview's fields share between create and update, keyed to
/// the field a client sent. A round needs a time, a kind and a format; the notes
/// are capped. The outcome is validated by the update slice alone, since a new
/// round is always pending.
/// </summary>
internal static class InterviewFieldValidation
{
    public static void Validate(IInterviewFields fields, ValidationErrors errors)
    {
        if (fields.ScheduledAt is null)
        {
            errors.Add("scheduledAt", "A scheduled time is required.");
        }

        if (!ParsesTo<InterviewType>(fields.Type))
        {
            errors.Add("type", "The type must be one of PhoneScreen, Technical, Behavioural, Onsite or Other.");
        }

        if (!ParsesTo<InterviewFormat>(fields.Format))
        {
            errors.Add("format", "The format must be one of Remote, Onsite or Phone.");
        }

        if (fields.Notes is { Length: > FieldRules.NotesMaxLength })
        {
            errors.Add("notes", $"The notes must be {FieldRules.NotesMaxLength} characters or fewer.");
        }
    }

    /// <summary>A required enum field: present and a defined member of <typeparamref name="TEnum"/>.</summary>
    public static bool ParsesTo<TEnum>(string? value) where TEnum : struct, Enum =>
        value is not null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed);
}
