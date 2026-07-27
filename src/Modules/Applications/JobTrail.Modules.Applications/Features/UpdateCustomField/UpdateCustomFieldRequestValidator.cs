namespace JobTrail.Modules.Applications.Features.UpdateCustomField;

/// <summary>
/// Shape-level checks on an update-custom-field request, keyed by field. Whether
/// the options suit the field's type cannot be judged here - the type lives on the
/// stored row, not in the request - so the handler makes that call once it has
/// loaded the definition.
/// </summary>
internal static class UpdateCustomFieldRequestValidator
{
    public static Dictionary<string, string[]>? Validate(UpdateCustomFieldRequest request)
    {
        var errors = new ValidationErrors();

        CustomFieldValidation.ValidateLabel(request.Label, errors);
        CustomFieldValidation.ValidateOptionShapes(request.Options, errors);

        return errors.ToResultOrNull();
    }
}
