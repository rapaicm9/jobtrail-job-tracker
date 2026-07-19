namespace JobTrail.Modules.Identity.Tests.Fakes;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock is fixed until a test advances it, so
/// expiry and rotation timing are deterministic rather than wall-clock-dependent.
/// </summary>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
