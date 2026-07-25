namespace JobTrail.Api.Idempotency;

/// <summary>
/// The replay cache's knobs. The defaults suit a hand-entry product reached from
/// a phone: a client retrying over a bad connection retries within seconds, and
/// nobody re-sends yesterday's request.
/// </summary>
internal sealed class IdempotencyOptions
{
    /// <summary>
    /// How long a completed response stays replayable. Long enough to cover a
    /// client that gave up, changed networks and came back.
    /// </summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long one in-flight request holds its key before the reservation
    /// lapses. Deliberately short: it is also the window in which a request that
    /// failed with a 5xx blocks its own retry, since after a server error nobody
    /// can say whether the write landed.
    /// </summary>
    public TimeSpan InFlightWindow { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>A bound on the header, so a client cannot post arbitrarily large Redis keys.</summary>
    public int MaxKeyLength { get; init; } = 128;

    /// <summary>
    /// The largest response body worth keeping. Everything this API returns is a
    /// small JSON document; a response past this is not stored, and the key is
    /// released so a retry re-runs rather than replaying something truncated.
    /// </summary>
    public int MaxBodyBytes { get; init; } = 64 * 1024;
}
