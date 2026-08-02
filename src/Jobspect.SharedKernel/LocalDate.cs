namespace Jobspect.SharedKernel;

/// <summary>
/// Turning an instant into the date it is <em>somewhere</em>. A date is only
/// meaningful in a place, so anything that stamps one on a user's record, or
/// decides which day or week that record falls in, resolves it through here.
/// <para>
/// It lives in the kernel because two modules now need the same answer and the two
/// must not drift apart. The Applications module stamps an application's applied
/// date with the caller's today; the Analytics module decides which week that date
/// belongs to from the same caller's today. Let those disagree by a day and an
/// application created this morning can fall outside the week its own creation
/// defined - a discrepancy nobody would think to look for.
/// </para>
/// <para>
/// Deliberately dependency-free: the caller resolves the user's zone (through
/// Identity's contract) and passes the id in, so the kernel stays ignorant of who
/// stores it.
/// </para>
/// </summary>
public static class LocalDate
{
    /// <summary>
    /// The calendar date it is at <paramref name="utcNow"/> in the given IANA zone.
    /// <para>
    /// Falls back to UTC when the id is absent or the host cannot resolve it. That
    /// is the lenient half of the rule and it is deliberate: a user whose timezone
    /// is missing or has been renamed out from under us still gets a date, off by
    /// at most a day, rather than a failed request. The stored id is validated when
    /// it is set, so this path is for a zone database that has moved on.
    /// </para>
    /// </summary>
    public static DateOnly TodayIn(DateTimeOffset utcNow, string? ianaTimeZoneId) =>
        ianaTimeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(ianaTimeZoneId, out var timezone)
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime)
            : DateOnly.FromDateTime(utcNow.UtcDateTime);
}
