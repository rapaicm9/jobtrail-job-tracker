using System.Security.Cryptography;
using JobTrail.Modules.Identity.Authentication;
using Microsoft.Extensions.Options;

namespace JobTrail.Modules.Identity.Tests;

/// <summary>
/// Builds <see cref="JwtOptions"/> around a freshly generated ES256 (P-256)
/// keypair, so tests sign and validate with real keys and never touch a
/// committed secret.
/// </summary>
internal static class TestKeys
{
    public static JwtOptions NewOptions(Action<JwtOptions>? configure = null)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new JwtOptions
        {
            PrivateKeyPem = ecdsa.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem(),
        };

        configure?.Invoke(options);
        return options;
    }

    public static IOptions<JwtOptions> Wrap(this JwtOptions options) => Options.Create(options);
}
