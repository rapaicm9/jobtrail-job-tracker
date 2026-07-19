using JobTrail.Modules.Identity.Authentication;

namespace JobTrail.Modules.Identity.Features;

/// <summary>
/// The one token payload every auth slice returns: register, login and refresh
/// all hand the client the same shape, so client-side handling is uniform. The
/// user id repeats the access token's <c>sub</c> claim so clients never need to
/// decode the JWT themselves.
/// </summary>
internal sealed record AuthTokensResponse(
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Manual mapping, per the no-AutoMapper rule.</summary>
internal static class AuthTokensMapping
{
    public static AuthTokensResponse ToResponse(this IssuedTokens tokens) => new(
        tokens.UserId.Value,
        tokens.Access.Value,
        tokens.Access.ExpiresAt,
        tokens.RefreshToken,
        tokens.RefreshTokenExpiresAt);
}
