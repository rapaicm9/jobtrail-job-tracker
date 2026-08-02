using System.Text.Json;

namespace Jobspect.Modules.Applications.Features.CreateApplication;

/// <summary>
/// The fields a client supplies to open an application. The pipeline stage is not
/// among them - a new application always starts at <c>Applied</c> - and neither is
/// the offer-decision deadline, which only becomes meaningful once an offer is on
/// the table (set later via update). Company follows the picker's two modes:
/// <see cref="CompanyId"/> to reference an existing company, or
/// <see cref="CompanyName"/> to create-or-reuse one by name (at most one of the two).
/// <see cref="AppliedDate"/> defaults to the caller's local today when omitted.
/// <para>
/// <see cref="CampaignId"/> is optional and names the campaign to open the
/// application in; omitted, it lands in the account's default. A Free account has
/// only its default to name, so the field costs it nothing - and no entitlement is
/// checked here, because placing an application in a campaign the account already
/// holds is not the paid capability. Opening a second campaign is.
/// </para>
/// <para>
/// <see cref="CustomFields"/> answers the fields the account defined for itself,
/// keyed by definition id, each value the raw JSON its field's type calls for.
/// Writing them needs the entitlement, which the handler checks - the endpoint
/// itself serves both tiers and so cannot carry the gate.
/// </para>
/// </summary>
internal sealed record CreateApplicationRequest(
    string? Role,
    Guid? CampaignId,
    Guid? CompanyId,
    string? CompanyName,
    MoneyRequest? Compensation,
    string? Location,
    string? WorkMode,
    string? PostingUrl,
    string? Source,
    DateOnly? AppliedDate,
    DateOnly? ApplicationDeadline,
    string? CvLabel,
    string? CoverLetterLabel,
    IReadOnlyDictionary<Guid, JsonElement>? CustomFields) : IApplicationFields;
