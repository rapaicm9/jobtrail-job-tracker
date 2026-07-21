using System.Security.Claims;
using JobTrail.SharedKernel;
using Microsoft.IdentityModel.JsonWebTokens;

namespace JobTrail.Modules.Identity.Features;

/// <summary>
/// Reads the authenticated account id off the request principal. One reader for
/// every authenticated slice, so the claim name and its parsing live in exactly
/// one place.
/// </summary>
internal static class CurrentUser
{
    /// <summary>
    /// The failure when a validated token carries no parseable subject - a
    /// malformed token that still cleared signature checks. Unauthorized, since
    /// the caller has no usable identity.
    /// </summary>
    public static readonly Error MissingSubject =
        Error.Unauthorized("auth.invalid_token", "The access token carries no usable subject.");

    /// <summary>
    /// True when the principal's <c>sub</c> claim parses to a <see cref="UserId"/>.
    /// MapInboundClaims is off, so the subject arrives as the raw <c>sub</c> name.
    /// </summary>
    public static bool TryGetId(this ClaimsPrincipal principal, out UserId userId) =>
        UserId.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
