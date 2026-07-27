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

    /// <summary>
    /// The caller sent custom-field values without the entitlement to write them.
    /// A 403 rather than a 404 or a 422: the request was understood and the caller
    /// is known, they simply may not do this part of it.
    /// <para>
    /// It cannot be an authorization policy on the route, the way the definition
    /// endpoints gate theirs - both tiers create and edit applications, and it is
    /// one part of the payload that is out of reach rather than the whole call.
    /// </para>
    /// </summary>
    public static readonly Error ValuesNotEntitled = Error.Forbidden(
        "custom_field.not_entitled",
        "Setting custom-field values requires Pro. Values already recorded are kept and stay readable.");

    /// <summary>
    /// A value was sent for a field the caller does not have. Another user's field
    /// reads the same way - the lookup is owner-scoped, so it is simply not theirs
    /// to answer.
    /// </summary>
    public static Error UnknownField(Guid id) =>
        Error.Validation("custom_field.unknown_field", $"No custom field with id {id} exists.");

    /// <summary>
    /// A value was sent for a retired field. Answers already recorded against it
    /// are kept and still read back; what stops is giving it new ones.
    /// </summary>
    public static Error ArchivedField(string label) =>
        Error.Validation("custom_field.archived_field", $"The custom field '{label}' is archived.");

    /// <summary>The value's JSON shape is not the one the field's type calls for.</summary>
    public static Error ValueTypeMismatch(string label, string expected) =>
        Error.Validation("custom_field.value_invalid", $"The value for '{label}' must be {expected}.");

    /// <summary>A select was answered with something that is not one of its options.</summary>
    public static Error UnknownOption(string label, string option) =>
        Error.Validation("custom_field.unknown_option", $"'{option}' is not an option of '{label}'.");

    /// <summary>
    /// The caller asked a list to filter or sort by a custom field without the
    /// entitlement to. Unlike reading values back - which stays open, or an account
    /// that lost Pro could not interpret its own applications - searching by them is
    /// the capability itself, and the plain list still returns everything.
    /// </summary>
    public static readonly Error QueryNotEntitled = Error.Forbidden(
        "custom_field.query_not_entitled",
        "Filtering and sorting by a custom field requires Pro.");
}
