using System.Text.Json;

namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// The full editable state of an application - a replace, so a field left off is
/// cleared, not kept. The pipeline stage is not here: moves go through the
/// transition endpoint, which the state machine guards. The offer-decision
/// deadline is settable now (it wasn't on create), but only once the application
/// has an offer - the handler enforces that. <see cref="AppliedDate"/> is required,
/// since a client editing the record already has it.
/// <para>
/// <see cref="CampaignId"/> is required for the same reason, and moving an
/// application between campaigns is what sending a different one means. It cannot
/// follow the "absent clears it" rule the other fields do - an application must
/// belong to a campaign - and the alternative reading, absent meaning "put it in
/// the default", would move applications every time a lax client left it off.
/// </para>
/// <para>
/// <see cref="CustomFields"/> is a replace like the rest for a caller entitled to
/// write it, and untouchable for one who is not - so an account that has lost the
/// entitlement keeps its answers by editing everything else around them. Values
/// are the raw JSON each field's type calls for; the handler is where they meet
/// the definitions that say what that is.
/// </para>
/// </summary>
internal sealed record UpdateApplicationRequest(
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
    DateOnly? OfferDecisionDeadline,
    string? CvLabel,
    string? CoverLetterLabel,
    IReadOnlyDictionary<Guid, JsonElement>? CustomFields) : IApplicationFields;
