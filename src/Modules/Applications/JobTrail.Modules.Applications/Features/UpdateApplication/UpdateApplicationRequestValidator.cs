namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// Shape-level checks on an update request, keyed by field. The built-in-field
/// rules are shared with create through <see cref="ApplicationFieldValidation"/>;
/// update adds that the applied date is required (create defaults it, a replace
/// must carry it). The offer-decision-deadline guard depends on the application's
/// current stage, so it lives in the handler, not here.
/// </summary>
internal static class UpdateApplicationRequestValidator
{
    public static Dictionary<string, string[]>? Validate(UpdateApplicationRequest request)
    {
        var errors = new ValidationErrors();
        ApplicationFieldValidation.Validate(request, errors);

        if (request.AppliedDate is null)
        {
            errors.Add("appliedDate", "An applied date is required.");
        }

        return errors.ToResultOrNull();
    }
}
