namespace Jobspect.Modules.Applications.Features.CreateCustomField;

/// <summary>
/// The fields to define a custom field. <see cref="Options"/> belongs to the
/// select types and must be absent for the rest. A new field is always active -
/// archiving is something that happens to a field later, not a way to create one.
/// </summary>
internal sealed record CreateCustomFieldRequest(
    string? Label,
    string? Type,
    IReadOnlyList<string>? Options);
