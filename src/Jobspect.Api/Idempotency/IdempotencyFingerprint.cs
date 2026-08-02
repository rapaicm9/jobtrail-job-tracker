using System.Security.Cryptography;
using System.Text;

namespace Jobspect.Api.Idempotency;

/// <summary>
/// What an idempotency key was claimed for. A key names one operation, so the
/// record carries a hash of the request that made it - method, path and body -
/// and a later request under the same key is only the same operation if it
/// hashes the same.
/// <para>
/// A pure function of the request, kept apart from the middleware so it can be
/// computed from either end: the request pipeline has a stream, a test has bytes,
/// and both have to arrive at the same value.
/// </para>
/// </summary>
internal static class IdempotencyFingerprint
{
    /// <summary>
    /// Fingerprints an in-flight request. Enables buffering first, because the
    /// body must still be readable by the model binding that follows.
    /// </summary>
    public static async Task<string> OfAsync(HttpRequest request, int bufferThreshold)
    {
        request.EnableBuffering(bufferThreshold);

        var bodyHash = await SHA256.HashDataAsync(request.Body);
        request.Body.Position = 0;

        return Combine(request.Method, $"{request.Path}{request.QueryString}", bodyHash);
    }

    public static string Of(string method, string pathAndQuery, ReadOnlySpan<byte> body) =>
        Combine(method, pathAndQuery, SHA256.HashData(body));

    private static string Combine(string method, string pathAndQuery, ReadOnlySpan<byte> bodyHash) =>
        Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{method}\n{pathAndQuery}\n{Convert.ToHexStringLower(bodyHash)}")));
}
