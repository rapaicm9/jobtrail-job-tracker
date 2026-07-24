using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>A compensation amount and its currency, as a client sees it.</summary>
internal sealed record MoneyResponse(decimal Amount, string Currency);

/// <summary>
/// An application as a client sees it: the built-in fields plus its pipeline
/// position, campaign and (optional) company. Deliberately narrower than the row -
/// no custom-field bag yet, and the enums travel as their names so the contract
/// doesn't leak storage ordinals. Returned by create, get, update and transition,
/// so a client never needs a follow-up read.
/// </summary>
internal sealed record ApplicationResponse(
    Guid Id,
    Guid CampaignId,
    Guid? CompanyId,
    string Stage,
    string Role,
    MoneyResponse? Compensation,
    string? Location,
    string? WorkMode,
    string? PostingUrl,
    string? Source,
    DateOnly AppliedDate,
    DateOnly? ApplicationDeadline,
    DateOnly? OfferDecisionDeadline,
    string? CvLabel,
    string? CoverLetterLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class ApplicationResponseMapping
{
    public static ApplicationResponse ToResponse(this Application application) => new(
        application.Id,
        application.CampaignId,
        application.CompanyId,
        application.Stage.ToString(),
        application.Role,
        application.Compensation is { } money ? new MoneyResponse(money.Amount, money.Currency) : null,
        application.Location,
        application.WorkMode?.ToString(),
        application.PostingUrl,
        application.Source,
        application.AppliedDate,
        application.ApplicationDeadline,
        application.OfferDecisionDeadline,
        application.CvLabel,
        application.CoverLetterLabel,
        application.CreatedAt,
        application.UpdatedAt);
}
