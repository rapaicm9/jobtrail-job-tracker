namespace Jobspect.Modules.Analytics.Features.GetOverview;

/// <summary>
/// The figures every account gets: how many applications it has recorded, and
/// where they are sitting now.
/// <para>
/// Deliberately narrow - no owner id, no campaign id, no row ids. Nothing here
/// identifies anything; a dashboard needs totals, and totals are all this returns.
/// </para>
/// </summary>
internal sealed record AnalyticsOverviewResponse(int TotalApplied, IReadOnlyList<PipelineStageCount> Pipeline);

/// <summary>
/// How many of the account's applications are at one stage.
/// <para>
/// The snapshot reports only the stages the account actually has. It does not pad
/// the set out with zeros, because doing so would mean this module declaring the
/// complete list of stages - which belongs to the module that owns the pipeline,
/// and is the one thing this one deliberately declines to know. A client renders
/// the board and therefore already holds that list; filling the gaps is work it is
/// doing anyway.
/// </para>
/// </summary>
internal sealed record PipelineStageCount(string Stage, int Count);
