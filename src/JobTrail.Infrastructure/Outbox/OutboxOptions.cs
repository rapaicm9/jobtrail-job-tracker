namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// The dispatcher's knobs. The defaults suit a hand-entry product: events are
/// rare, so a one-second poll is far below anything a user would notice, and a
/// small batch keeps a single slow handler from holding rows locked.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>How often to look for owed events.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How many rows one claim takes. The dispatcher keeps claiming until a batch comes back short.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    /// After this many failures a row is left alone rather than retried forever.
    /// It stays unprocessed and visible, which is the state an operator wants to
    /// find - the alternative is an event that quietly disappears.
    /// </summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>The delay before a first retry; it doubles with each failure, up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The ceiling on the backoff, so a broken handler is retried steadily rather than never.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a processed row is kept before pruning - long enough to answer "was it delivered?".</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(7);
}
