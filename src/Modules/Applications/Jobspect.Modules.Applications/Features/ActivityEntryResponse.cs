using Jobspect.Modules.Applications.Domain;

namespace Jobspect.Modules.Applications.Features;

/// <summary>
/// One row of an application's timeline as a client sees it. The kind, the two
/// stage ends and the transition kind travel as their names rather than storage
/// ordinals. Which of the nullable members are filled follows from the kind: a
/// creation entry carries only the stage it entered at, a stage change carries
/// both ends and how it moved, a note carries its text.
/// </summary>
internal sealed record ActivityEntryResponse(
    Guid Id,
    ActivityKind Kind,
    DateTimeOffset OccurredAt,
    Stage? FromStage,
    Stage? ToStage,
    TransitionKind? TransitionKind,
    string? Note);

internal static class ActivityEntryResponseMapping
{
    public static ActivityEntryResponse ToResponse(this ActivityLogEntry entry) => new(
        entry.Id,
        entry.Kind,
        entry.CreatedAt,
        entry.FromStage,
        entry.ToStage,
        entry.TransitionKind,
        entry.Note);
}
