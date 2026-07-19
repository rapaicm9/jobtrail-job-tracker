using System.Buffers.Text;
using System.Security.Cryptography;

namespace JobTrail.Modules.Identity.Authentication;

/// <summary>
/// Generates the opaque refresh-token secret: 256 bits of cryptographically
/// secure randomness, URL-safe Base64 so it survives HTTP headers and JSON
/// unescaped. The token carries no structure - its only job is to be unguessable
/// and to match a stored hash.
/// </summary>
internal static class OpaqueTokenGenerator
{
    private const int TokenSizeInBytes = 32; // 256-bit

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenSizeInBytes));
}
