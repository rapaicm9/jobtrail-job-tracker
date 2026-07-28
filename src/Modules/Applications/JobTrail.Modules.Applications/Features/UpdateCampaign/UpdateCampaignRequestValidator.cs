namespace JobTrail.Modules.Applications.Features.UpdateCampaign;

/// <summary>Shape-level checks on an update-campaign request, keyed by field.</summary>
internal static class UpdateCampaignRequestValidator
{
    public static Dictionary<string, string[]>? Validate(UpdateCampaignRequest request)
    {
        var errors = new ValidationErrors();

        CampaignValidation.ValidateName(request.Name, errors);

        return errors.ToResultOrNull();
    }
}
