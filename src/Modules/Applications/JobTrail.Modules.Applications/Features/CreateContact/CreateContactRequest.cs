namespace JobTrail.Modules.Applications.Features.CreateContact;

/// <summary>
/// The fields to record a contact. At least one of <see cref="ApplicationId"/> and
/// <see cref="CompanyId"/> must be present - a contact is always attached to
/// something. Both links, when given, must be the caller's own.
/// </summary>
internal sealed record CreateContactRequest(
    Guid? ApplicationId,
    Guid? CompanyId,
    string? Name,
    string? Role,
    string? Email,
    string? Phone,
    string? Notes) : IContactFields;
