namespace JobTrail.Api.Idempotency;

/// <summary>
/// What one idempotency key holds: first a reservation, then the response it
/// produced. A record with no <see cref="Status"/> is a request still running -
/// that distinction is what turns a concurrent duplicate into a 409 rather than a
/// second execution.
/// </summary>
/// <param name="Holder">
/// Who owns this reservation. A request only completes or releases a key while
/// the stored record is still the one it wrote: its own reservation may have
/// lapsed and been taken by a later request, and overwriting that one's record
/// would hand somebody else's caller the wrong response.
/// </param>
/// <param name="Fingerprint">
/// The request this key was claimed for. A second request under the same key with
/// a different fingerprint is a client reusing a key by mistake, which has to be
/// refused rather than silently answered with the first request's result.
/// </param>
internal sealed record IdempotencyRecord(
    string Holder,
    string Fingerprint,
    int? Status,
    string? ContentType,
    string? Location,
    string? Body)
{
    public bool IsCompleted => Status is not null;

    public static IdempotencyRecord Reserve(string fingerprint) =>
        new(Guid.CreateVersion7().ToString("N"), fingerprint, null, null, null, null);

    public IdempotencyRecord Complete(int status, string? contentType, string? location, string? body) =>
        this with { Status = status, ContentType = contentType, Location = location, Body = body };
}
