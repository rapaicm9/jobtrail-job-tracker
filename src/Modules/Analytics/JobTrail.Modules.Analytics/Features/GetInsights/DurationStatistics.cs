namespace JobTrail.Modules.Analytics.Features.GetInsights;

/// <summary>
/// Summarises a set of durations for display.
/// <para>
/// <b>The median, not the mean.</b> A job search produces exactly the distribution
/// an average is worst at describing: forty applications answered inside a week
/// and one answered after five months. That single reply moves a mean by weeks,
/// and the figure stops describing anything that happened to anyone. The median
/// stays where the experience is.
/// </para>
/// </summary>
internal static class DurationStatistics
{
    /// <summary>
    /// The median of <paramref name="durations"/> in fractional days, and how many
    /// values it was computed from.
    /// <para>
    /// Null when there is nothing to compute from. Not zero: an account with no
    /// answers yet has <em>no</em> time-to-response, and reporting zero days would
    /// be a number the reader would believe.
    /// </para>
    /// <para>
    /// The sample count travels with it so a client can decline to draw a median
    /// built from two applications, which is a figure the arithmetic is happy to
    /// produce and nobody should be shown.
    /// </para>
    /// </summary>
    public static (double? Median, int Samples) MedianDays(IEnumerable<TimeSpan> durations)
    {
        var days = durations.Select(duration => duration.TotalDays).Order().ToArray();

        if (days.Length == 0)
        {
            return (null, 0);
        }

        var middle = days.Length / 2;
        var median = days.Length % 2 == 1
            ? days[middle]
            : (days[middle - 1] + days[middle]) / 2;

        return (median, days.Length);
    }

    /// <summary>
    /// A proportion of <paramref name="total"/>, or null when there is nothing to
    /// take a proportion of - for the same reason the median is null on an empty
    /// set. A brand-new account has no response rate, and showing it 0% would be
    /// both false and discouraging.
    /// </summary>
    public static double? Rate(int count, int total) => total == 0 ? null : (double)count / total;
}
