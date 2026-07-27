using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// A custom-field definition as its owner sees it. The type travels as its name
/// rather than a storage ordinal, and the options are always present - an empty
/// list for the types that take none - so a client never has to tell "no options"
/// from "not sent".
/// </summary>
internal sealed record CustomFieldResponse(
    Guid Id,
    string Label,
    string Type,
    IReadOnlyList<string> Options,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class CustomFieldResponseMapping
{
    public static CustomFieldResponse ToResponse(this CustomFieldDefinition definition) => new(
        definition.Id,
        definition.Label,
        definition.Type.ToString(),
        definition.Options,
        definition.IsArchived,
        definition.CreatedAt,
        definition.UpdatedAt);
}
