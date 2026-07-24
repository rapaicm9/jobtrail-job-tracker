using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// An application as a list row - what a board or table shows at a glance, not the
/// full record. Narrower than <see cref="ApplicationResponse"/> on purpose: enough
/// to render and sort the list, with the detail fetched per application on open.
/// </summary>
internal sealed record ApplicationSummaryResponse(
    Guid Id,
    Guid CampaignId,
    Guid? CompanyId,
    string Stage,
    string Role,
    string? WorkMode,
    DateOnly AppliedDate,
    DateOnly? ApplicationDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class ApplicationSummaryMapping
{
    public static ApplicationSummaryResponse ToSummary(this Application application) => new(
        application.Id,
        application.CampaignId,
        application.CompanyId,
        application.Stage.ToString(),
        application.Role,
        application.WorkMode?.ToString(),
        application.AppliedDate,
        application.ApplicationDeadline,
        application.CreatedAt,
        application.UpdatedAt);
}
