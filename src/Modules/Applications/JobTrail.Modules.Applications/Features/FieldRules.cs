namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Shared shape rules and length caps for the application slices' validators.
/// The caps mirror the column lengths configured in
/// <see cref="Persistence.ApplicationsDbContext"/>, so a value that validates
/// here also fits the store.
/// </summary>
internal static class FieldRules
{
    public const int RoleMaxLength = 200;
    public const int LocationMaxLength = 200;
    public const int PostingUrlMaxLength = 2048;
    public const int SourceMaxLength = 100;
    public const int LabelMaxLength = 200;
    public const int CompanyNameMaxLength = 200;
    public const int CurrencyLength = 3;

    /// <summary>An absolute http(s) URL - the only thing worth storing as a posting link.</summary>
    public static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// A three-letter ISO 4217-shaped currency code (letters only). The code is
    /// stored verbatim; v1 does no FX and keeps no currency registry, so this is a
    /// shape check, not a membership check.
    /// </summary>
    public static bool IsCurrencyCode(string value) =>
        value.Length == CurrencyLength && value.All(char.IsAsciiLetter);
}
