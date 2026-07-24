using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// One row of an application's timeline as a client sees it. The kind, the two
/// stage ends and the transition kind travel as their names rather than storage
/// ordinals. Which of the nullable members are filled follows from the kind: a
/// creation entry carries only the stage it entered at, a stage change carries
/// both ends and how it moved, a note carries its text.
/// </summary>
internal sealed record ActivityEntryResponse(
    Guid Id,
    string Kind,
    DateTimeOffset OccurredAt,
    string? FromStage,
    string? ToStage,
    string? TransitionKind,
    string? Note);

internal static class ActivityEntryResponseMapping
{
    public static ActivityEntryResponse ToResponse(this ActivityLogEntry entry) => new(
        entry.Id,
        entry.Kind.ToString(),
        entry.CreatedAt,
        entry.FromStage?.ToString(),
        entry.ToStage?.ToString(),
        entry.TransitionKind?.ToString(),
        entry.Note);
}
