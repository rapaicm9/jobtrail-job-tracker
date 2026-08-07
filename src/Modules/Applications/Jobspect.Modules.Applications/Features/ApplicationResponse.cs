using System.Text.Json;
using Jobspect.Modules.Applications.Domain;

namespace Jobspect.Modules.Applications.Features;

/// <summary>A compensation amount and its currency, as a client sees it.</summary>
internal sealed record MoneyResponse(decimal Amount, string Currency);

/// <summary>
/// An application as a client sees it: the built-in fields plus its pipeline
/// position, campaign, (optional) company and the account's own custom-field
/// answers. Deliberately narrower than the row, and the enums travel as their
/// names so the contract doesn't leak storage ordinals. Returned by create, get,
/// update and transition, so a client never needs a follow-up read.
/// <para>
/// The domain enums are carried as themselves rather than pre-stringified: the
/// host's string-enum converter writes the same names, and holding the type is
/// what lets the described contract state which names exist.
/// </para>
/// <para>
/// <see cref="CustomFields"/> is returned to every caller, entitlement or not.
/// Writing values is the paid capability; reading back what is already recorded
/// is not, or an account that lost the entitlement could not make sense of its
/// own applications.
/// </para>
/// </summary>
internal sealed record ApplicationResponse(
    Guid Id,
    Guid CampaignId,
    Guid? CompanyId,
    Stage Stage,
    string Role,
    MoneyResponse? Compensation,
    string? Location,
    WorkMode? WorkMode,
    string? PostingUrl,
    string? Source,
    DateOnly AppliedDate,
    DateOnly? ApplicationDeadline,
    DateOnly? OfferDecisionDeadline,
    string? CvLabel,
    string? CoverLetterLabel,
    IReadOnlyDictionary<Guid, JsonElement> CustomFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class ApplicationResponseMapping
{
    public static ApplicationResponse ToResponse(this Application application) => new(
        application.Id,
        application.CampaignId,
        application.CompanyId,
        application.Stage,
        application.Role,
        application.Compensation is { } money ? new MoneyResponse(money.Amount, money.Currency) : null,
        application.Location,
        application.WorkMode,
        application.PostingUrl,
        application.Source,
        application.AppliedDate,
        application.ApplicationDeadline,
        application.OfferDecisionDeadline,
        application.CvLabel,
        application.CoverLetterLabel,
        application.CustomFieldValues.Values,
        application.CreatedAt,
        application.UpdatedAt);
}
