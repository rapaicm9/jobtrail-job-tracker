namespace JobTrail.Modules.Identity.Features.UpdateAccount;

/// <summary>
/// The mutable slice of the profile. Only the timezone is editable in v1: the
/// email is the login and changing it is a bigger flow (uniqueness, re-verify)
/// deferred to its own feature.
/// </summary>
internal sealed record UpdateAccountRequest(string? TimeZoneId);
