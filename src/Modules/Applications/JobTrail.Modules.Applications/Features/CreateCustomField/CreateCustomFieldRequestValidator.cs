namespace JobTrail.Modules.Applications.Features.CreateCustomField;

/// <summary>
/// Shape-level checks on a create-custom-field request, keyed by field. Create is
/// the one path that can judge the options against the type without a database
/// read, because the type is right there in the request.
/// </summary>
internal static class CreateCustomFieldRequestValidator
{
    public static Dictionary<string, string[]>? Validate(CreateCustomFieldRequest request)
    {
        var errors = new ValidationErrors();

        CustomFieldValidation.ValidateLabel(request.Label, errors);
        CustomFieldValidation.ValidateOptionShapes(request.Options, errors);

        if (CustomFieldValidation.ParseType(request.Type) is not { } type)
        {
            errors.Add("type", $"The type must be one of {CustomFieldValidation.TypeNames}.");
        }
        else if (CustomFieldValidation.OptionsProblemFor(type, request.Options) is { } problem)
        {
            errors.Add("options", problem);
        }

        return errors.ToResultOrNull();
    }
}
