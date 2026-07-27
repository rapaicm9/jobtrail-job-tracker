using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>Failures raised by the custom-field slices.</summary>
internal static class CustomFieldErrors
{
    /// <summary>
    /// No custom field with this id is owned by the caller. A field owned by
    /// another user is reported the same way - a 404, never a 403 - so ownership
    /// stays unobservable.
    /// </summary>
    public static Error NotFound(Guid id) =>
        Error.NotFound("custom_field.not_found", $"No custom field with id {id} exists.");

    /// <summary>
    /// The caller already has an unarchived field by this name. A conflict rather
    /// than a validation failure: the request was well formed, the name is taken.
    /// Archiving the original frees the name again.
    /// </summary>
    public static Error LabelTaken(string label) =>
        Error.Conflict("custom_field.label_taken", $"A custom field named '{label}' already exists.");

    /// <summary>
    /// The account is at its custom-field limit. Archived fields count: their
    /// recorded values are kept, so they still occupy a slot.
    /// </summary>
    public static Error LimitReached(int limit) =>
        Error.Conflict(
            "custom_field.limit_reached",
            $"An account may hold {limit} custom fields, archived ones included.");

    /// <summary>
    /// The options given do not suit the field's type - a select with none, or
    /// anything else with some. The type is fixed at creation, so on an update this
    /// is always the options that are wrong.
    /// </summary>
    public static Error OptionsNotAllowed(string message) =>
        Error.Validation("custom_field.options_invalid", message);
}
