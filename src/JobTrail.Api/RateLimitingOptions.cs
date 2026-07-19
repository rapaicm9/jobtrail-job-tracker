namespace JobTrail.Api;

/// <summary>
/// Tunable limits, bound from the <c>RateLimiting</c> section. The defaults are
/// deliberately generous for a single-user tracker's SPA traffic while still
/// making credential stuffing and token grinding uneconomical; ops override
/// them per environment without a rebuild.
/// </summary>
internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Per-IP budget across the whole API surface.</summary>
    public int GlobalPermitLimit { get; set; } = 100;

    public TimeSpan GlobalWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Sliding-window resolution; more segments = smoother refill.</summary>
    public int GlobalSegmentsPerWindow { get; set; } = 6;

    /// <summary>Per-IP budget on <c>/api/v1/identity/*</c> - the brute-force surface.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    public TimeSpan AuthWindow { get; set; } = TimeSpan.FromMinutes(1);
}
