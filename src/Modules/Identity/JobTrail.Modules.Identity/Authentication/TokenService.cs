using JobTrail.SharedKernel;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// The module's token-issuance entry point, called by the auth endpoints slice.
/// It composes access-token minting with refresh-token issuance and rotation so a
/// caller gets a matched pair and never has to orchestrate the two halves.
/// </summary>
internal sealed class TokenService(
    AccessTokenIssuer accessTokenIssuer,
    RefreshTokenService refreshTokenService,
    IUserTokenVersionReader tokenVersionReader)
{
    /// <summary>Issues a fresh access + refresh pair for a just-authenticated user.</summary>
    public async Task<IssuedTokens> IssueAsync(
        UserId userId, int tokenVersion, string? deviceLabel, CancellationToken cancellationToken)
    {
        var access = accessTokenIssuer.Issue(userId, tokenVersion);
        var refresh = await refreshTokenService.IssueAsync(userId, deviceLabel, cancellationToken);
        return new IssuedTokens(userId, access, refresh.RawToken, refresh.ExpiresAt);
    }

    /// <summary>
    /// Rotates a presented refresh token and mints a matching new access token
    /// carrying the account's current token version. Fails if the token is
    /// invalid/expired/reused, or if the account no longer exists.
    /// </summary>
    public async Task<Result<IssuedTokens>> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var rotation = await refreshTokenService.RotateAsync(rawRefreshToken, cancellationToken);
        if (rotation.IsFailure)
        {
            return rotation.Error;
        }

        var rotated = rotation.Value;
        var tokenVersion = await tokenVersionReader.GetTokenVersionAsync(rotated.UserId, cancellationToken);
        if (tokenVersion is null)
        {
            // The account vanished between rotation and lookup - refuse rather than mint a token for a ghost.
            return Error.Unauthorized("refresh_token.user_not_found", "The account for this token no longer exists.");
        }

        var access = accessTokenIssuer.Issue(rotated.UserId, tokenVersion.Value);
        return new IssuedTokens(rotated.UserId, access, rotated.RawToken, rotated.ExpiresAt);
    }
}
