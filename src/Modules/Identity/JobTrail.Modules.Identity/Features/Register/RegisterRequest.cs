namespace JobTrail.Modules.Identity.Features.Register;

/// <summary>
/// Everything needed to open an account. The timezone is optional and defaults
/// to UTC; the device label names the refresh-token row so the user can later
/// recognize the session ("Firefox on Linux").
/// </summary>
internal sealed record RegisterRequest(
    string? Email,
    string? Password,
    string? TimeZoneId,
    string? DeviceLabel);
