using System.Net.Mail;

namespace JobTrail.Modules.Identity.Features;

/// <summary>Field checks shared by more than one slice validator.</summary>
internal static class FieldRules
{
    /// <summary>Column limit on <c>refresh_tokens.device_label</c>.</summary>
    public const int DeviceLabelMaxLength = 128;

    /// <summary>Column limit on <c>users.time_zone_id</c>.</summary>
    public const int TimeZoneIdMaxLength = 64;

    /// <summary>Identity's default cap on the normalized email column.</summary>
    public const int EmailMaxLength = 256;

    public static bool IsPlausibleEmail(string email) =>
        email.Length <= EmailMaxLength && MailAddress.TryCreate(email, out _);

    /// <summary>
    /// True for IANA zone ids only ("Europe/Belgrade"). Windows ids resolve on
    /// Windows and ICU-enabled Linux alike, so a positive lookup alone is not
    /// enough - anything that round-trips as a Windows id is rejected, except
    /// "UTC", which is both a Windows id and a valid IANA name.
    /// </summary>
    public static bool IsIanaTimeZone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out _)
        && (!TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out _) || id is "UTC");
}
