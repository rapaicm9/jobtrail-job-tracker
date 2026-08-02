namespace Jobspect.Modules.Applications.Features;

/// <summary>
/// The shape rules a campaign's fields share between create and update. Whether the
/// name is already taken is not among them: that is the database's answer, taken
/// from the unique index rather than a read beforehand, which two creates racing on
/// one name would both pass.
/// </summary>
internal static class CampaignValidation
{
    public static void ValidateName(string? name, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "A name is required.");
        }
        else if (name.Length > FieldRules.CampaignNameMaxLength)
        {
            errors.Add("name", $"The name must be {FieldRules.CampaignNameMaxLength} characters or fewer.");
        }
    }
}
