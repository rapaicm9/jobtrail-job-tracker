namespace JobTrail.Modules.Applications.Features.UpdateInterview;

/// <summary>
/// The full editable state of an interview round - a replace, so a field left off
/// is cleared. Unlike create it carries the <see cref="Outcome"/>: this is where a
/// round's result is recorded once it has happened.
/// </summary>
internal sealed record UpdateInterviewRequest(
    DateTimeOffset? ScheduledAt,
    string? Type,
    string? Format,
    string? Outcome,
    string? Notes) : IInterviewFields;
