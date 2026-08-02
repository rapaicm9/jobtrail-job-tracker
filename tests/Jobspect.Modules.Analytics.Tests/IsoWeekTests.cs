using Jobspect.Modules.Analytics.Features;
using Shouldly;

namespace Jobspect.Modules.Analytics.Tests;

/// <summary>
/// Where this module cuts a week. Small arithmetic with one genuinely awkward case,
/// and both the weekly trend and the weekly goal are wrong together if it is wrong.
/// </summary>
public sealed class IsoWeekTests
{
    [Fact]
    public void A_monday_is_the_start_of_its_own_week()
    {
        var monday = new DateOnly(2026, 7, 27);

        monday.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        IsoWeek.WeekStarting(monday).ShouldBe(monday);
    }

    [Fact]
    public void A_sunday_belongs_to_the_week_that_is_ending_not_the_one_beginning()
    {
        // The case the arithmetic exists for. DayOfWeek starts its week on Sunday
        // and numbers it zero, so the naive shift sends a Sunday six days forward
        // into the week it has just finished rather than back to its Monday.
        var sunday = new DateOnly(2026, 8, 2);

        sunday.DayOfWeek.ShouldBe(DayOfWeek.Sunday);
        IsoWeek.WeekStarting(sunday).ShouldBe(new DateOnly(2026, 7, 27));
    }

    [Theory]
    [InlineData(7, 28)] // Tuesday
    [InlineData(7, 29)] // Wednesday
    [InlineData(7, 30)] // Thursday
    [InlineData(7, 31)] // Friday
    [InlineData(8, 1)]  // Saturday
    [InlineData(8, 2)]  // Sunday
    public void Every_day_of_one_week_reports_the_same_monday(int month, int day)
    {
        IsoWeek.WeekStarting(new DateOnly(2026, month, day))
            .ShouldBe(new DateOnly(2026, 7, 27));
    }

    [Fact]
    public void A_week_reaches_back_across_a_month_boundary()
    {
        // Wednesday 1 July 2026; its Monday is in June.
        IsoWeek.WeekStarting(new DateOnly(2026, 7, 1)).ShouldBe(new DateOnly(2026, 6, 29));
    }

    [Fact]
    public void A_week_reaches_back_across_a_year_boundary()
    {
        // Friday 1 January 2027; its Monday is in the previous year.
        IsoWeek.WeekStarting(new DateOnly(2027, 1, 1)).ShouldBe(new DateOnly(2026, 12, 28));
    }

    [Fact]
    public void A_leap_day_counts_back_like_any_other()
    {
        // Sunday 29 February 2032 - the awkward day inside the awkward case.
        var leapDay = new DateOnly(2032, 2, 29);

        leapDay.DayOfWeek.ShouldBe(DayOfWeek.Sunday);
        IsoWeek.WeekStarting(leapDay).ShouldBe(new DateOnly(2032, 2, 23));
    }
}
