namespace JobTrail.Modules.Identity.Features.Register;

/// <summary>
/// Shape-level checks before the handler runs. The password rules mirror the
/// policy configured on Identity in <see cref="IdentityModule"/> - Identity's
/// validators remain the enforcement of record at CreateAsync; repeating the
/// rules here gets the caller a field-keyed 422 listing every unmet rule at
/// once, instead of a policy failure after the fact.
/// </summary>
internal static class RegisterRequestValidator
{
    public const int MinPasswordLength = 8;

    public static Dictionary<string, string[]>? Validate(RegisterRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add("email", "An email address is required.");
        }
        else if (!FieldRules.IsPlausibleEmail(request.Email))
        {
            errors.Add("email", "The email address is not valid.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors.Add("password", "A password is required.");
        }
        else
        {
            ValidatePasswordRules(request.Password, errors);
        }

        if (request.TimeZoneId is not null)
        {
            if (request.TimeZoneId.Length > FieldRules.TimeZoneIdMaxLength
                || !FieldRules.IsIanaTimeZone(request.TimeZoneId))
            {
                errors.Add("timeZoneId", "The timezone must be a valid IANA id, e.g. \"Europe/Belgrade\".");
            }
        }

        if (request.DeviceLabel is { Length: > FieldRules.DeviceLabelMaxLength })
        {
            errors.Add("deviceLabel", $"The device label must be {FieldRules.DeviceLabelMaxLength} characters or fewer.");
        }

        return errors.ToResultOrNull();
    }

    private static void ValidatePasswordRules(string password, ValidationErrors errors)
    {
        if (password.Length < MinPasswordLength)
        {
            errors.Add("password", $"The password must be at least {MinPasswordLength} characters long.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("password", "The password must contain at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("password", "The password must contain at least one lowercase letter.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("password", "The password must contain at least one digit.");
        }

        // Identity's definition of "special": anything that is not a letter or digit.
        if (password.All(char.IsLetterOrDigit))
        {
            errors.Add("password", "The password must contain at least one special character.");
        }
    }
}
