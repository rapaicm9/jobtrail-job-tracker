namespace Jobspect.Modules.Applications.Features.CreateCampaign;

/// <summary>Shape-level checks on a create-campaign request, keyed by field.</summary>
internal static class CreateCampaignRequestValidator
{
    public static Dictionary<string, string[]>? Validate(CreateCampaignRequest request)
    {
        var errors = new ValidationErrors();

        CampaignValidation.ValidateName(request.Name, errors);

        return errors.ToResultOrNull();
    }
}
