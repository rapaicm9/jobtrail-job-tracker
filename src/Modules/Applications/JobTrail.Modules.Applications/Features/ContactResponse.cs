using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// A contact as its owner sees it. Carries the personal data back only to the user
/// who saved it - never to anyone else, never onto an event or a log. The role
/// travels as its name rather than a storage ordinal.
/// </summary>
internal sealed record ContactResponse(
    Guid Id,
    Guid? ApplicationId,
    Guid? CompanyId,
    string Name,
    string? Role,
    string? Email,
    string? Phone,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class ContactResponseMapping
{
    public static ContactResponse ToResponse(this Contact contact) => new(
        contact.Id,
        contact.ApplicationId,
        contact.CompanyId,
        contact.Name,
        contact.Role?.ToString(),
        contact.Email,
        contact.Phone,
        contact.Notes,
        contact.CreatedAt,
        contact.UpdatedAt);
}
