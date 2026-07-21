using JobTrail.Modules.Identity.Domain;

namespace JobTrail.Modules.Identity.Features;

/// <summary>
/// The account as its owner sees it. Deliberately narrower than the row - no
/// password hash, security stamp or token version - so the read surface can
/// never widen by accident. The read and update slices both return this shape,
/// so a client handles one representation.
/// </summary>
internal sealed record AccountResponse(
    Guid UserId,
    string Email,
    string TimeZoneId,
    DateTimeOffset CreatedAt);

/// <summary>Manual mapping, per the no-AutoMapper rule.</summary>
internal static class AccountMapping
{
    public static AccountResponse ToResponse(this ApplicationUser user) => new(
        user.Id,
        // Email is non-null for every account the register slice opens; the
        // store guarantees it, so surfacing it unguarded is safe.
        user.Email!,
        user.TimeZoneId,
        user.CreatedAt);
}
