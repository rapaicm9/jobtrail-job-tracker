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
    public const int ContactNameMaxLength = 200;
    public const int EmailMaxLength = 320;
    public const int PhoneMaxLength = 40;
    public const int NotesMaxLength = 2000;

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

    /// <summary>
    /// A plausibly-shaped email address: one <c>@</c> with something either side and
    /// no spaces. A shape check, not delivery verification - v1 never sends mail, so
    /// this only rejects the obviously-wrong.
    /// </summary>
    public static bool IsPlausibleEmail(string value) =>
        value.Count(c => c == '@') == 1
        && value[0] != '@'
        && value[^1] != '@'
        && !value.Any(char.IsWhiteSpace);

    /// <summary>
    /// A plausibly-shaped phone number: digits and the usual separators
    /// (<c>+ - ( ) . space</c>), with at least a few digits. Kept loose on purpose -
    /// v1 stores the number, it doesn't dial it.
    /// </summary>
    public static bool IsPlausiblePhone(string value) =>
        value.Count(char.IsDigit) >= 3 && value.All(c => char.IsDigit(c) || c is '+' or '-' or '(' or ')' or '.' or ' ');
}
