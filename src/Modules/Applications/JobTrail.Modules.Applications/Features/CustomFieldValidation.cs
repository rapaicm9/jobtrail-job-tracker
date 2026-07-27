using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The shape rules a custom field's fields share between create and update.
/// <para>
/// Split in two on purpose. The label and the options' own shape can be judged
/// from the request alone, so they are checked in the validators and come back
/// field-keyed. Whether the options <em>suit the type</em> cannot: the type is
/// fixed at creation and so is absent from an update request, which has to read it
/// off the stored row first. That rule therefore lives here as one check both
/// paths call - the create validator, which knows the type from the request, and
/// the update handler, which has just loaded it.
/// </para>
/// </summary>
internal static class CustomFieldValidation
{
    public static void ValidateLabel(string? label, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            errors.Add("label", "A label is required.");
        }
        else if (label.Length > FieldRules.CustomFieldLabelMaxLength)
        {
            errors.Add("label", $"The label must be {FieldRules.CustomFieldLabelMaxLength} characters or fewer.");
        }
    }

    /// <summary>
    /// What can be said about the options without knowing the type: each one is
    /// real text of a sane length, there are not too many, and none repeats.
    /// </summary>
    public static void ValidateOptionShapes(IReadOnlyList<string>? options, ValidationErrors errors)
    {
        if (options is null or { Count: 0 })
        {
            return;
        }

        if (options.Count > FieldRules.CustomFieldOptionsMaxCount)
        {
            errors.Add("options", $"A field may offer {FieldRules.CustomFieldOptionsMaxCount} options at most.");
            return;
        }

        if (options.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("options", "An option cannot be blank.");
        }

        if (options.Any(option => option?.Length > FieldRules.CustomFieldOptionMaxLength))
        {
            errors.Add(
                "options", $"An option must be {FieldRules.CustomFieldOptionMaxLength} characters or fewer.");
        }

        // Compared the way a person reads them: two options differing only in case
        // are the same choice offered twice.
        var distinct = options
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinct != options.Count(option => !string.IsNullOrWhiteSpace(option)))
        {
            errors.Add("options", "The options must be different from one another.");
        }
    }

    /// <summary>
    /// Whether these options suit this type, as a message, or <c>null</c> when the
    /// pairing is fine. A select needs something to choose from; every other type
    /// has nothing to do with a list of choices, and silently keeping one would
    /// leave a field that changes meaning if its type ever could.
    /// </summary>
    public static string? OptionsProblemFor(CustomFieldType type, IReadOnlyList<string>? options)
    {
        var provided = options?.Count(option => !string.IsNullOrWhiteSpace(option)) ?? 0;

        return (RequiresOptions(type), provided) switch
        {
            (true, 0) => $"A {type} field must offer at least one option.",
            (false, > 0) => $"A {type} field does not take options.",
            _ => null,
        };
    }

    public static bool RequiresOptions(CustomFieldType type) =>
        type is CustomFieldType.SingleSelect or CustomFieldType.MultiSelect;

    /// <summary>
    /// The options as they should be stored: trimmed, blanks dropped, order kept -
    /// the user chose the order they appear in. Empty for the types that take none,
    /// so a non-select never carries a stray list.
    /// </summary>
    public static string[] CleanOptions(CustomFieldType type, IReadOnlyList<string>? options) =>
        RequiresOptions(type) && options is not null
            ? [.. options.Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => option.Trim())]
            : [];

    /// <summary>
    /// The type a client named, or <c>null</c> if it named nothing recognizable.
    /// Case-insensitive, like the other enums a client sends.
    /// </summary>
    public static CustomFieldType? ParseType(string? type) =>
        Enum.TryParse<CustomFieldType>(type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    /// <summary>The types spelled out, for the message a client gets when it names none of them.</summary>
    public static string TypeNames => string.Join(", ", Enum.GetNames<CustomFieldType>());
}
