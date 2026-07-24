using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// An interview round as a client sees it. The kind, format and outcome travel as
/// their names rather than storage ordinals. Returned by create, get and update,
/// so a client needs no follow-up read.
/// </summary>
internal sealed record InterviewResponse(
    Guid Id,
    Guid ApplicationId,
    DateTimeOffset ScheduledAt,
    string Type,
    string Format,
    string Outcome,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class InterviewResponseMapping
{
    public static InterviewResponse ToResponse(this Interview interview) => new(
        interview.Id,
        interview.ApplicationId,
        interview.ScheduledAt,
        interview.Type.ToString(),
        interview.Format.ToString(),
        interview.Outcome.ToString(),
        interview.Notes,
        interview.CreatedAt,
        interview.UpdatedAt);
}
