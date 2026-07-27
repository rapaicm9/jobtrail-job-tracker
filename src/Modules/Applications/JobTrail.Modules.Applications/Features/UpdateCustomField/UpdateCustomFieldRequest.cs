namespace JobTrail.Modules.Applications.Features.UpdateCustomField;

/// <summary>
/// The editable fields of a custom field. There is deliberately no type here: it
/// is fixed at creation, because the values already recorded were given under it,
/// and a field that cannot change is better left out of the payload than accepted
/// and refused.
/// <para>
/// <see cref="IsArchived"/> is how a field is retired and brought back - archiving
/// is a property of the field rather than a move through some lifecycle, so it
/// travels with the rest rather than earning an endpoint of its own.
/// </para>
/// </summary>
internal sealed record UpdateCustomFieldRequest(
    string? Label,
    IReadOnlyList<string>? Options,
    bool IsArchived);
