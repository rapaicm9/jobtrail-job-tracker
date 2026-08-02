namespace Jobspect.Modules.Analytics.Features.GetInsights;

/// <summary>
/// The paid dashboard, in one payload. Every figure here is aggregated - counts,
/// proportions and medians - so nothing in it identifies an application, a company
/// or a person.
/// </summary>
internal sealed record AnalyticsInsightsResponse(
    FunnelResponse Funnel,
    RatesResponse Rates,
    TimingResponse Timing,
    IReadOnlyList<TrendPoint> Trend,
    BreakdownsResponse Breakdowns);

/// <summary>
/// How many applications got as far as each step. Counted from the timestamps
/// recorded when each stage was first reached, not from where applications are
/// sitting now - a forward move may skip a stage and a terminal one erases the
/// path, so the current stage is no witness to how far something got.
/// </summary>
internal sealed record FunnelResponse(
    int Total, int Responded, int ReachedScreening, int ReachedInterview, int ReachedOffer);

/// <summary>
/// The funnel as proportions. <b>All three are over the total applied</b>, not
/// over the previous step - "of everything I applied to, 18% got an interview" is
/// the question people are asking. A client wanting step-to-step conversion has
/// the counts to compute it from, and does not have to guess the denominator.
/// <para>
/// Null when the account has recorded nothing. Not zero: it has no response rate
/// yet, and 0% is a number the reader would believe.
/// </para>
/// </summary>
internal sealed record RatesResponse(double? Response, double? Interview, double? Offer);

/// <summary>
/// How long things took, in fractional days, as medians. Each carries the number
/// of applications behind it so a client can decline to draw one built from two.
/// </summary>
internal sealed record TimingResponse(
    double? MedianDaysToFirstResponse,
    int FirstResponseSamples,
    double? MedianDaysToOffer,
    int OfferSamples,
    IReadOnlyList<StageDuration> TimeInStage);

/// <summary>
/// How long applications sat at one stage before moving on. Only completed
/// intervals count, so an application still at this stage contributes nothing.
/// </summary>
internal sealed record StageDuration(string Stage, double? MedianDays, int Samples);

/// <summary>
/// How many applications were sent in one week, keyed by the Monday it began.
/// <para>
/// Sparse - weeks with nothing are absent rather than zero. The server has no idea
/// what range a client intends to chart, so filling the axis belongs to whoever
/// chose it.
/// </para>
/// </summary>
internal sealed record TrendPoint(DateOnly WeekStarting, int Count);

/// <summary>Where applications came from, and how they were to be worked.</summary>
internal sealed record BreakdownsResponse(
    IReadOnlyList<BreakdownSlice> Source, IReadOnlyList<BreakdownSlice> WorkMode);

/// <summary>
/// One slice of a breakdown.
/// <para>
/// A null <see cref="Value"/> means the user did not record one, and it is
/// included deliberately - unlike the pipeline snapshot, which drops applications
/// whose stage is not yet known. The two nulls are not the same thing: there, null
/// is a passing artifact of the order events arrived in; here it is a fact about
/// what the user chose to fill in, and a breakdown that hid it would understate
/// its own total.
/// </para>
/// </summary>
internal sealed record BreakdownSlice(string? Value, int Count);
