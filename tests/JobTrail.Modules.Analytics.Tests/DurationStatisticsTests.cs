using JobTrail.Modules.Analytics.Features.GetInsights;
using Shouldly;

namespace JobTrail.Modules.Analytics.Tests;

/// <summary>
/// The median and the rate, and the one behaviour both share: nothing to compute
/// from is null, never zero.
/// </summary>
public sealed class DurationStatisticsTests
{
    [Fact]
    public void An_odd_number_of_durations_takes_the_middle_one()
    {
        var (median, samples) = DurationStatistics.MedianDays(Days(2, 9, 4));

        median.ShouldBe(4);
        samples.ShouldBe(3);
    }

    [Fact]
    public void An_even_number_takes_the_midpoint_of_the_two_in_the_middle()
    {
        var (median, samples) = DurationStatistics.MedianDays(Days(1, 4, 6, 9));

        median.ShouldBe(5);
        samples.ShouldBe(4);
    }

    [Fact]
    public void One_duration_is_its_own_median()
    {
        var (median, samples) = DurationStatistics.MedianDays(Days(7));

        median.ShouldBe(7);
        samples.ShouldBe(1);
    }

    [Fact]
    public void Nothing_to_measure_is_null_rather_than_zero()
    {
        // An account with no replies yet has no time-to-response. Zero days would
        // be a number the reader would believe.
        var (median, samples) = DurationStatistics.MedianDays([]);

        median.ShouldBeNull();
        samples.ShouldBe(0);
    }

    [Fact]
    public void One_long_wait_does_not_move_the_median_the_way_it_would_move_a_mean()
    {
        // The reason this is a median at all: nine replies inside a fortnight and
        // one after most of a year. The mean would report about seven weeks.
        var (median, _) = DurationStatistics.MedianDays(Days(3, 4, 5, 6, 7, 8, 9, 10, 11, 300));

        median.ShouldBe(7.5);
    }

    [Fact]
    public void Fractional_days_survive()
    {
        var (median, _) = DurationStatistics.MedianDays([TimeSpan.FromHours(36)]);

        median.ShouldBe(1.5);
    }

    [Theory]
    [InlineData(1, 4, 0.25)]
    [InlineData(0, 4, 0)]
    [InlineData(3, 3, 1)]
    public void A_rate_is_the_count_over_the_total(int count, int total, double expected) =>
        DurationStatistics.Rate(count, total).ShouldBe(expected);

    [Fact]
    public void A_rate_over_nothing_is_null_rather_than_zero() =>
        DurationStatistics.Rate(0, 0).ShouldBeNull();

    private static TimeSpan[] Days(params double[] days) => [.. days.Select(TimeSpan.FromDays)];
}
