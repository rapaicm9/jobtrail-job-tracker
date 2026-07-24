namespace JobTrail.Modules.Applications.Features.CreateContact;

/// <summary>
/// Shape-level checks on a create-contact request, keyed by field. The rules are
/// shared with update through <see cref="ContactFieldValidation"/>; ownership of
/// the linked application and company is the handler's job.
/// </summary>
internal static class CreateContactRequestValidator
{
    public static Dictionary<string, string[]>? Validate(CreateContactRequest request)
    {
        var errors = new ValidationErrors();
        ContactFieldValidation.Validate(request, errors);
        return errors.ToResultOrNull();
    }
}
