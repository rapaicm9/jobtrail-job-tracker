namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// Shape-level checks on an update request, keyed by field. The built-in-field
/// rules are shared with create through <see cref="ApplicationFieldValidation"/>;
/// update adds that the applied date and the campaign are required (create
/// defaults both, a replace must carry them). Whether the campaign is the caller's
/// own needs the database, and the offer-decision-deadline guard depends on the
/// application's current stage, so both live in the handler.
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

        if (request.CampaignId is null)
        {
            errors.Add("campaignId", "A campaign is required.");
        }

        return errors.ToResultOrNull();
    }
}
