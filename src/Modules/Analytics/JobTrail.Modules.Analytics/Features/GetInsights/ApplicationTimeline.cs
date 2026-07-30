namespace JobTrail.Modules.Analytics.Features.GetInsights;

/// <summary>
/// One application's base row, reduced to the facts the paid figures are computed
/// from. Materialised in bulk and then aggregated in memory - see
/// <see cref="GetInsightsHandler"/> for why the work happens here rather than in
/// SQL.
/// </summary>
/// <param name="AppliedDate">
/// Null while the submission has not been delivered yet, which excludes the row
/// from every figure measured from it.
/// </param>
internal sealed record ApplicationTimeline(
    DateOnly? AppliedDate,
    DateTimeOffset? FirstResponseAt,
    DateTimeOffset? ReachedScreeningAt,
    DateTimeOffset? ReachedInterviewAt,
    DateTimeOffset? ReachedOfferAt,
    DateTimeOffset? ClosedAt,
    string? Source,
    string? WorkMode)
{
    /// <summary>
    /// When the application was applied for, as an instant.
    /// <para>
    /// The date is the user's own - it is what they said, not when they recorded it
    /// - and a date only means something in a place. Reading it at midnight UTC is
    /// therefore an approximation, and a deliberate one: every figure measured from
    /// it is reported in days, where being out by part of a day cannot change what
    /// the number says.
    /// </para>
    /// </summary>
    public DateTimeOffset? AppliedAt =>
        AppliedDate is { } date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
}
