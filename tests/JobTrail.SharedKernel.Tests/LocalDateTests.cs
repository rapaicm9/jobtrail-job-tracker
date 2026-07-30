using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.SharedKernel.Tests;

/// <summary>
/// Which day it is depends on where you are, and this is the one place the system
/// decides that. Two modules read the answer - one stamps it on an application, the
/// other decides which week that application falls in - so a change here moves both.
/// </summary>
public sealed class LocalDateTests
{
    /// <summary>
    /// Late evening in UTC, which is already tomorrow in the far east and still
    /// yesterday in the far west. The instant is one; the date is three.
    /// </summary>
    private static readonly DateTimeOffset LateEvening =
        new(2026, 7, 30, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_instant_is_a_different_date_east_and_west()
    {
        LocalDate.TodayIn(LateEvening, "Pacific/Kiritimati")
            .ShouldBe(new DateOnly(2026, 7, 31));

        LocalDate.TodayIn(LateEvening, "Pacific/Midway")
            .ShouldBe(new DateOnly(2026, 7, 30));

        LocalDate.TodayIn(LateEvening, "Etc/UTC")
            .ShouldBe(new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void A_zone_west_of_the_line_can_still_be_on_the_previous_day()
    {
        // Just past midnight UTC, so most of the Americas have not got there yet.
        var justPastMidnight = new DateTimeOffset(2026, 7, 31, 0, 30, 0, TimeSpan.Zero);

        LocalDate.TodayIn(justPastMidnight, "America/Los_Angeles")
            .ShouldBe(new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void An_unknown_zone_falls_back_to_utc_rather_than_failing()
    {
        // A zone id that was valid when it was stored and has since been retired,
        // or one this host's zone database has never heard of. A date off by at
        // most a day beats a request that cannot be answered at all.
        LocalDate.TodayIn(LateEvening, "Mars/Olympus_Mons")
            .ShouldBe(new DateOnly(2026, 7, 30));
    }

    [Fact]
    public void No_zone_at_all_falls_back_to_utc()
    {
        LocalDate.TodayIn(LateEvening, ianaTimeZoneId: null)
            .ShouldBe(new DateOnly(2026, 7, 30));
    }
}
