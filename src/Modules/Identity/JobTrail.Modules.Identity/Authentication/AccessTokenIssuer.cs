using JobTrail.SharedKernel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Mints the short-lived access token: an ES256-signed JWT whose only claims are
/// the opaque account id (<c>sub</c>), a unique token id (<c>jti</c>) and the
/// token version. No PII in the payload (ADR-0003) - email and everything else
/// stay server-side.
/// </summary>
internal sealed class AccessTokenIssuer(
    ISigningKeyProvider keyProvider,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>The claim carrying the value bumped on global logout.</summary>
    public const string TokenVersionClaim = "token_version";

    // JsonWebTokenHandler is thread-safe and stateless; one instance serves all issuance.
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(UserId userId, int tokenVersion)
    {
        var jwt = options.Value;
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt + jwt.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
                [TokenVersionClaim] = tokenVersion,
            },
            SigningCredentials = keyProvider.SigningCredentials,
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }
}
