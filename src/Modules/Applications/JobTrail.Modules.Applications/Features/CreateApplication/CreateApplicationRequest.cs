namespace JobTrail.Modules.Applications.Features.CreateApplication;

/// <summary>
/// The fields a client supplies to open an application. The pipeline stage is not
/// among them - a new application always starts at <c>Applied</c> - and neither is
/// the offer-decision deadline, which only becomes meaningful once an offer is on
/// the table (set later via update). Company follows the picker's two modes:
/// <see cref="CompanyId"/> to reference an existing company, or
/// <see cref="CompanyName"/> to create-or-reuse one by name (at most one of the two).
/// <see cref="AppliedDate"/> defaults to the caller's local today when omitted.
/// </summary>
internal sealed record CreateApplicationRequest(
    string? Role,
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
    string? CoverLetterLabel) : IApplicationFields;
