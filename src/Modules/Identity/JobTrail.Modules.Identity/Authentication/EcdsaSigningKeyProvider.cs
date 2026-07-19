using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Loads the ES256 keypair from <see cref="JwtOptions"/> once, lazily, on first
/// use - so a host with no keys configured still starts, since nothing signs or
/// validates a token until an auth endpoint runs. The <see cref="ECDsa"/>
/// instances live for the application lifetime (held via the keys below), which
/// is why this is registered as a singleton.
/// </summary>
internal sealed class EcdsaSigningKeyProvider : ISigningKeyProvider
{
    private readonly Lazy<SigningCredentials> _signingCredentials;
    private readonly Lazy<SecurityKey> _publicKey;

    public EcdsaSigningKeyProvider(IOptions<JwtOptions> options)
    {
        var jwt = options.Value;
        _signingCredentials = new Lazy<SigningCredentials>(() => CreateSigningCredentials(jwt.PrivateKeyPem));
        _publicKey = new Lazy<SecurityKey>(() => CreateKey(jwt.PublicKeyPem, "public", "Identity:Jwt:PublicKeyPem"));
    }

    public SigningCredentials SigningCredentials => _signingCredentials.Value;

    public SecurityKey PublicKey => _publicKey.Value;

    private static SigningCredentials CreateSigningCredentials(string privateKeyPem)
    {
        var key = CreateKey(privateKeyPem, "private", "Identity:Jwt:PrivateKeyPem");
        return new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
    }

    private static ECDsaSecurityKey CreateKey(string pem, string kind, string configPath)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                $"No ES256 {kind} key configured ({configPath}). Set it via user-secrets in development or an environment variable in production.");
        }

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return new ECDsaSecurityKey(ecdsa);
    }
}
