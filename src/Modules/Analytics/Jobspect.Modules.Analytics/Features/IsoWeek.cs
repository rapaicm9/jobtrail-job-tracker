namespace Jobspect.Modules.Analytics.Features;

/// <summary>
/// Where this module cuts a week. Shared rather than settled twice: the weekly
/// trend plots one point per week and the weekly goal measures progress inside the
/// current one, and two panels of the same dashboard disagreeing about which week a
/// date is in is the kind of contradiction a user notices and nobody can explain.
/// </summary>
internal static class IsoWeek
{
    /// <summary>
    /// The Monday of the week a date falls in - ISO weeks, as Postgres counts them.
    /// <para>
    /// The shift is written out rather than subtracted from <see cref="DayOfWeek"/>
    /// directly because that enum starts its week on Sunday with the value zero, so
    /// the naive arithmetic sends a Sunday forward six days into the week it has
    /// just ended instead of back to the Monday that began it.
    /// </para>
    /// </summary>
    public static DateOnly WeekStarting(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
