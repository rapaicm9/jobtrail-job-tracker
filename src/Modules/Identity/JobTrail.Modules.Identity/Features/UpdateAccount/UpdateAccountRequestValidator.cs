namespace JobTrail.Modules.Identity.Features.UpdateAccount;

/// <summary>
/// Shape-level checks for a profile update. The timezone is required here
/// (unlike registration, where it defaults) - an update states the new value
/// outright, and the same IANA rule as the register slice applies.
/// </summary>
internal static class UpdateAccountRequestValidator
{
    public static Dictionary<string, string[]>? Validate(UpdateAccountRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            errors.Add("timeZoneId", "A timezone is required.");
        }
        else if (request.TimeZoneId.Length > FieldRules.TimeZoneIdMaxLength
            || !FieldRules.IsIanaTimeZone(request.TimeZoneId))
        {
            errors.Add("timeZoneId", "The timezone must be a valid IANA id, e.g. \"Europe/Belgrade\".");
        }

        return errors.ToResultOrNull();
    }
}
