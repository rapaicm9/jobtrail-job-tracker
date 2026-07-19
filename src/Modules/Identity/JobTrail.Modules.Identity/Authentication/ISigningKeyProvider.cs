using Microsoft.IdentityModel.Tokens;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Supplies the ES256 keys for the token model. The private key signs access
/// tokens (Api only); the public key validates them (Api now, the Worker later),
/// so a consumer that only verifies never has to hold the secret (ADR-0003).
/// </summary>
internal interface ISigningKeyProvider
{
    /// <summary>ES256 signing credentials backed by the private key.</summary>
    SigningCredentials SigningCredentials { get; }

    /// <summary>The public key, for building token-validation parameters.</summary>
    SecurityKey PublicKey { get; }
}
