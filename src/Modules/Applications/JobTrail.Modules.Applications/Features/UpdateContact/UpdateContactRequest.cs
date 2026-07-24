namespace JobTrail.Modules.Applications.Features.UpdateContact;

/// <summary>
/// The full editable state of a contact - a replace, so a field left off is
/// cleared. At least one of the two links must remain, and both, when given, must
/// be the caller's own.
/// </summary>
internal sealed record UpdateContactRequest(
    Guid? ApplicationId,
    Guid? CompanyId,
    string? Name,
    string? Role,
    string? Email,
    string? Phone,
    string? Notes) : IContactFields;
