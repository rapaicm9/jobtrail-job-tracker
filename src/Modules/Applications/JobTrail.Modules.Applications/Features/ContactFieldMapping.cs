using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Turns a validated contact request's role into the stored enum. The free-text
/// fields reuse <see cref="ApplicationFieldMapping.Clean"/> - trim, blank becomes
/// absent - so contacts and applications treat text the one way.
/// </summary>
internal static class ContactFieldMapping
{
    public static ContactRole? ParseRole(string? role) =>
        role is null ? null : Enum.Parse<ContactRole>(role, ignoreCase: true);
}
