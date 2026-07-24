namespace JobTrail.Modules.Applications.Features.CreateInterview;

/// <summary>
/// The fields to schedule an interview round. No outcome: a new round is always
/// pending, and its result is recorded later via update. The application it
/// belongs to comes from the route, not the body.
/// </summary>
internal sealed record CreateInterviewRequest(
    DateTimeOffset? ScheduledAt,
    string? Type,
    string? Format,
    string? Notes) : IInterviewFields;
