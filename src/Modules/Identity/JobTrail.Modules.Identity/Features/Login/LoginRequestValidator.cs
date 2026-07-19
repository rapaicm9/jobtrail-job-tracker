namespace JobTrail.Modules.Identity.Features.Login;

/// <summary>
/// Presence checks only. Deliberately no password-rule echo here: a login
/// attempt is judged against the stored credentials, and detailing the policy
/// to an unauthenticated caller helps only an attacker.
/// </summary>
internal static class LoginRequestValidator
{
    public static Dictionary<string, string[]>? Validate(LoginRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add("email", "An email address is required.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors.Add("password", "A password is required.");
        }

        if (request.DeviceLabel is { Length: > FieldRules.DeviceLabelMaxLength })
        {
            errors.Add("deviceLabel", $"The device label must be {FieldRules.DeviceLabelMaxLength} characters or fewer.");
        }

        return errors.ToResultOrNull();
    }
}
