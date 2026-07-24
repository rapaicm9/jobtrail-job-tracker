using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Turns validated interview request strings into the stored enums. Each method
/// trusts the shape <see cref="InterviewFieldValidation"/> already vetted; the
/// notes reuse <see cref="ApplicationFieldMapping.Clean"/>.
/// </summary>
internal static class InterviewFieldMapping
{
    public static InterviewType ParseType(string? type) => Enum.Parse<InterviewType>(type!, ignoreCase: true);

    public static InterviewFormat ParseFormat(string? format) => Enum.Parse<InterviewFormat>(format!, ignoreCase: true);

    public static InterviewOutcome ParseOutcome(string? outcome) => Enum.Parse<InterviewOutcome>(outcome!, ignoreCase: true);
}
