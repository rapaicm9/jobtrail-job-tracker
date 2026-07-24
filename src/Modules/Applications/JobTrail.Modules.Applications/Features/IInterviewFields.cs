namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// The fields a create and an update interview request share, so one set of shape
/// rules (<see cref="InterviewFieldValidation"/>) applies to both. The outcome is
/// not here: a round is created as pending and its outcome only set on update, so
/// it lives on the update request alone.
/// </summary>
internal interface IInterviewFields
{
    DateTimeOffset? ScheduledAt { get; }

    string? Type { get; }

    string? Format { get; }

    string? Notes { get; }
}
