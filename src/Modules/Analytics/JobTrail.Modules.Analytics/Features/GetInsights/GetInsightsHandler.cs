using JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;
using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Features.GetInsights;

/// <summary>
/// Every paid figure, from one read of the caller's base rows.
/// <para>
/// The nine figures need five different shapes of aggregation - scalar counts,
/// medians, per-stage intervals, a weekly grouping and two categorical ones - but
/// they all read the same narrow slice of the same rows. So the rows are
/// materialised once and everything is computed here rather than in SQL.
/// </para>
/// <para>
/// That is the bet ADR 0016 already took, one step further: aggregating on read is
/// affordable at the few hundred rows one job search reaches. It buys two things
/// worth having - a median needs no ordered-set aggregate, and the interval logic
/// becomes plain code a unit test can drive rather than SQL that only a database
/// can answer for. The cost is that this is linear in the account's applications;
/// if that ever stops being cheap, the aggregation moves into SQL behind the same
/// response.
/// </para>
/// </summary>
internal sealed class GetInsightsHandler(AnalyticsDbContext dbContext, IEntitlementQuery entitlements)
{
    public async Task<Result<AnalyticsInsightsResponse>> HandleAsync(
        UserId ownerId, Guid? campaignId, CancellationToken cancellationToken)
    {
        // The route policy has already refused an unentitled caller, so this never
        // fires through the endpoint. It fires if the endpoint is ever mapped
        // without its policy, or if this handler is called from somewhere that is
        // not that endpoint - the gate belongs to the operation, not to one route.
        if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.FullAnalytics, cancellationToken))
        {
            return AnalyticsErrors.FullAnalyticsNotEntitled;
        }

        var rows = dbContext.ApplicationFacts
            .AsNoTracking()
            .Where(facts => facts.OwnerId == ownerId);

        // Ungated, per ADR 0005: an account with one campaign gets the same figures
        // back, and a campaign that is not the caller's matches nothing.
        if (campaignId is { } campaign)
        {
            rows = rows.Where(facts => facts.CampaignId == campaign);
        }

        var timelines = await rows
            .Select(facts => new ApplicationTimeline(
                facts.AppliedDate,
                facts.FirstResponseAt,
                facts.ReachedScreeningAt,
                facts.ReachedInterviewAt,
                facts.ReachedOfferAt,
                facts.ClosedAt,
                facts.Source,
                facts.WorkMode))
            .ToListAsync(cancellationToken);

        return new AnalyticsInsightsResponse(
            Funnel(timelines),
            Rates(timelines),
            Timing(timelines),
            Trend(timelines),
            new BreakdownsResponse(
                Breakdown(timelines, timeline => timeline.Source),
                Breakdown(timelines, timeline => timeline.WorkMode)));
    }

    private static FunnelResponse Funnel(List<ApplicationTimeline> timelines) =>
        new(
            timelines.Count,
            timelines.Count(timeline => timeline.FirstResponseAt is not null),
            timelines.Count(timeline => timeline.ReachedScreeningAt is not null),
            timelines.Count(timeline => timeline.ReachedInterviewAt is not null),
            timelines.Count(timeline => timeline.ReachedOfferAt is not null));

    private static RatesResponse Rates(List<ApplicationTimeline> timelines)
    {
        var total = timelines.Count;

        return new RatesResponse(
            DurationStatistics.Rate(timelines.Count(t => t.FirstResponseAt is not null), total),
            DurationStatistics.Rate(timelines.Count(t => t.ReachedInterviewAt is not null), total),
            DurationStatistics.Rate(timelines.Count(t => t.ReachedOfferAt is not null), total));
    }

    private static TimingResponse Timing(List<ApplicationTimeline> timelines)
    {
        var (toFirstResponse, responseSamples) =
            DurationStatistics.MedianDays(Elapsed(timelines, timeline => timeline.FirstResponseAt));

        var (toOffer, offerSamples) =
            DurationStatistics.MedianDays(Elapsed(timelines, timeline => timeline.ReachedOfferAt));

        // Grouped rather than looped over a fixed list of stages: an application
        // contributes only to stages it actually passed through, so the stages that
        // appear are the ones the account has evidence for - the same rule the
        // pipeline snapshot follows.
        var timeInStage = timelines
            .SelectMany(StageIntervals.Of)
            .GroupBy(interval => interval.Stage)
            .Select(group =>
            {
                var (median, samples) = DurationStatistics.MedianDays(group.Select(interval => interval.Duration));
                return new StageDuration(group.Key, median, samples);
            })
            .OrderBy(stage => PipelineStages.Order(stage.Stage))
            .ThenBy(stage => stage.Stage, StringComparer.Ordinal)
            .ToArray();

        return new TimingResponse(toFirstResponse, responseSamples, toOffer, offerSamples, timeInStage);
    }

    /// <summary>
    /// How long each application took to reach some moment, measured from the date
    /// the user says they applied. Applications that never got there, or whose
    /// submission has not arrived yet, contribute nothing.
    /// </summary>
    private static IEnumerable<TimeSpan> Elapsed(
        List<ApplicationTimeline> timelines, Func<ApplicationTimeline, DateTimeOffset?> reached) =>
        timelines
            .Where(timeline => timeline.AppliedAt is not null && reached(timeline) is not null)
            .Select(timeline => reached(timeline)!.Value - timeline.AppliedAt!.Value)
            .Where(elapsed => elapsed >= TimeSpan.Zero);

    /// <summary>
    /// Applications per week, by the Monday each week began. An application whose
    /// applied date is not known yet cannot be placed on the axis, so it is absent
    /// rather than guessed at.
    /// </summary>
    private static TrendPoint[] Trend(List<ApplicationTimeline> timelines) =>
        [.. timelines
            .Where(timeline => timeline.AppliedDate is not null)
            .GroupBy(timeline => IsoWeek.WeekStarting(timeline.AppliedDate!.Value))
            .OrderBy(week => week.Key)
            .Select(week => new TrendPoint(week.Key, week.Count()))];

    /// <summary>
    /// One categorical breakdown, largest slice first. The "not recorded" slice is
    /// included: it is a fact about what the user filled in, and a breakdown that
    /// dropped it would not add up to the total beside it.
    /// </summary>
    private static BreakdownSlice[] Breakdown(
        List<ApplicationTimeline> timelines, Func<ApplicationTimeline, string?> value) =>
        [.. timelines
            .GroupBy(value)
            .Select(group => new BreakdownSlice(group.Key, group.Count()))
            .OrderByDescending(slice => slice.Count)
            .ThenBy(slice => slice.Value, StringComparer.Ordinal)];
}
