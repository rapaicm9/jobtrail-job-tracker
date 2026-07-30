namespace JobTrail.Modules.Applications.Features.ChartCustomField;

/// <summary>
/// The order statistics a number field is summarised by.
/// <para>
/// A small local copy rather than something shared: the Analytics module computes
/// medians of its own over durations, and the way to avoid the duplication would
/// be to put statistics into the shared kernel, which holds primitives - ids,
/// <c>Result</c>, money, the clock - and should not grow a maths library to save
/// ten lines.
/// </para>
/// </summary>
internal static class AnswerStatistics
{
    /// <summary>
    /// Minimum, quartiles, median and maximum of <paramref name="values"/>, which
    /// must not be empty - the caller has already decided there is something to
    /// summarise.
    /// <para>
    /// Quartiles are taken by linear interpolation between the two neighbouring
    /// values, so a set of four does not have to pretend one of its members is the
    /// quartile. With a single value every statistic is that value, which is the
    /// honest answer rather than a special case.
    /// </para>
    /// </summary>
    public static (decimal Min, decimal Lower, decimal Median, decimal Upper, decimal Max) Summarise(
        IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();

        return (sorted[0], Quantile(sorted, 0.25), Quantile(sorted, 0.5), Quantile(sorted, 0.75), sorted[^1]);
    }

    private static decimal Quantile(decimal[] sorted, double quantile)
    {
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var position = quantile * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);

        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (decimal)(position - lower));
    }
}
