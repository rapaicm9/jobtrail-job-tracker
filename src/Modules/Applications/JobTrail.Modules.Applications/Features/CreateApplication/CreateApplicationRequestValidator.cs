namespace JobTrail.Modules.Applications.Features.CreateApplication;

/// <summary>
/// Shape-level checks on a create request, keyed by field so the client gets a
/// 422 pointing at what it sent. The built-in-field rules are shared with update
/// through <see cref="ApplicationFieldValidation"/>; create adds nothing of its
/// own here (an omitted applied date is defaulted, not rejected). Ownership of a
/// referenced company and resolving the default campaign are the handler's job -
/// they need the database.
/// </summary>
internal static class CreateApplicationRequestValidator
{
    public static Dictionary<string, string[]>? Validate(CreateApplicationRequest request)
    {
        var errors = new ValidationErrors();
        ApplicationFieldValidation.Validate(request, errors);
        return errors.ToResultOrNull();
    }
}
