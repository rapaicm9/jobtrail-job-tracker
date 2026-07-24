namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// The full editable state of an application - a replace, so a field left off is
/// cleared, not kept. The pipeline stage is not here: moves go through the
/// transition endpoint, which the state machine guards. The offer-decision
/// deadline is settable now (it wasn't on create), but only once the application
/// has an offer - the handler enforces that. <see cref="AppliedDate"/> is required,
/// since a client editing the record already has it.
/// </summary>
internal sealed record UpdateApplicationRequest(
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
    DateOnly? OfferDecisionDeadline,
    string? CvLabel,
    string? CoverLetterLabel) : IApplicationFields;
