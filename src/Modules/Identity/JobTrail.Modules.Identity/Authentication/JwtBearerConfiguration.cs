using System.Globalization;
using System.Security.Claims;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// The validation half of ADR-0003, applied to the host's JwtBearer scheme:
/// public key only, full rigor (issuer, audience, lifetime, signature, and the
/// ES256 algorithm pinned), plus the per-request token-version check that makes
/// global logout effective before the access token expires.
/// </summary>
internal static class JwtBearerConfiguration
{
    public static void Configure(JwtBearerOptions bearer, ISigningKeyProvider keys, IOptions<JwtOptions> options)
    {
        var jwt = options.Value;

        // Claims stay exactly as issued - no remapping of `sub` onto the legacy
        // SOAP-era claim types.
        bearer.MapInboundClaims = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            // A resolver rather than IssuerSigningKey: the PEM loads on the
            // first token validation, preserving the key provider's lazy
            // contract - a host without keys still starts and serves health
            // checks and anonymous endpoints.
            IssuerSigningKeyResolver = (_, _, _, _) => [keys.PublicKey],
            // Single-writer of both tokens and clocks; the default 5-minute
            // skew would be half the access token's lifetime.
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };

        bearer.Events = new JwtBearerEvents { OnTokenValidated = EnforceCurrentTokenVersionAsync };
    }

    /// <summary>
    /// Rejects structurally valid tokens whose version no longer matches the
    /// account - the DB read per authenticated request is the price of making
    /// "log out everywhere" mean now, and it also drops tokens of deleted users.
    /// </summary>
    private static async Task EnforceCurrentTokenVersionAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var version = context.Principal?.FindFirstValue(AccessTokenIssuer.TokenVersionClaim);

        if (!UserId.TryParse(sub, out var userId)
            || !int.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenVersion))
        {
            context.Fail("The token is malformed.");
            return;
        }

        var reader = context.HttpContext.RequestServices.GetRequiredService<IUserTokenVersionReader>();
        var current = await reader.GetTokenVersionAsync(userId, context.HttpContext.RequestAborted);

        if (current != tokenVersion)
        {
            context.Fail("The token is no longer valid.");
        }
    }
}
