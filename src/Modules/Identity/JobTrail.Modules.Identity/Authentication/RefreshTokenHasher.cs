using System.Security.Cryptography;
using System.Text;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// The one place a refresh token is turned into the value stored server-side.
/// The raw token never touches the database - only this SHA-256 digest does - so
/// a database leak cannot yield a usable token. A fast hash (not a slow password
/// hash) is correct here: the input is 256 bits of CSPRNG output, so there is no
/// low-entropy secret to brute-force.
/// </summary>
internal static class RefreshTokenHasher
{
    public static byte[] Hash(string rawToken) => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
}
