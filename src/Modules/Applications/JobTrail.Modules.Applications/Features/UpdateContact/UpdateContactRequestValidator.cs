namespace JobTrail.Modules.Applications.Features.UpdateContact;

/// <summary>
/// Shape-level checks on an update-contact request, keyed by field - the same
/// rules as create, through <see cref="ContactFieldValidation"/>. Ownership of the
/// linked application and company is the handler's job.
/// </summary>
internal static class UpdateContactRequestValidator
{
    public static Dictionary<string, string[]>? Validate(UpdateContactRequest request)
    {
        var errors = new ValidationErrors();
        ContactFieldValidation.Validate(request, errors);
        return errors.ToResultOrNull();
    }
}
