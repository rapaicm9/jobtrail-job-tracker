namespace JobTrail.Modules.Identity.Features.Refresh;

internal static class RefreshRequestValidator
{
    public static Dictionary<string, string[]>? Validate(RefreshRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            errors.Add("refreshToken", "A refresh token is required.");
        }

        return errors.ToResultOrNull();
    }
}
