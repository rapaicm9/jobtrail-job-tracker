namespace JobTrail.Modules.Identity.Features.Logout;

internal static class LogoutRequestValidator
{
    public static Dictionary<string, string[]>? Validate(LogoutRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            errors.Add("refreshToken", "A refresh token is required.");
        }

        return errors.ToResultOrNull();
    }
}
