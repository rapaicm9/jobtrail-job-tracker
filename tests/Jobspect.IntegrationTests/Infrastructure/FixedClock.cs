namespace Jobspect.IntegrationTests.Infrastructure;

/// <summary>
/// A clock that reads whatever the test says it reads.
/// <para>
/// The host still runs on the real one - no service is replaced in this fixture -
/// so this is for the handlers a test constructs itself. That is the only place it
/// is needed: the rule it exists to pin down is that a reminder whose instant has
/// already passed is never armed, and "already" has to be a decision the test makes
/// rather than a race it hopes to win.
/// </para>
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
