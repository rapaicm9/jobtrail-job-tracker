using Jobspect.Modules.Analytics.Features.ProjectApplicationFacts;

namespace Jobspect.Modules.Analytics.Features.GetInsights;

/// <summary>How long one application spent at one stage.</summary>
internal readonly record struct StageInterval(string Stage, TimeSpan Duration);

/// <summary>
/// Turns one application's checkpoints into the time it spent at each stage.
/// <para>
/// The whole method is: take the entries that are known, put them in order, and
/// read each consecutive pair as "arrived here, left for there". Skips then need
/// no handling of their own - an application that jumped from Applied to Offer
/// simply has no Screening checkpoint, so its Applied interval runs all the way to
/// the offer and it contributes nothing at all to Screening, which is correct
/// because it was never there.
/// </para>
/// </summary>
internal static class StageIntervals
{
    /// <summary>
    /// The intervals this application contributes.
    /// <para>
    /// The stage an application is <em>currently</em> sitting at yields nothing:
    /// that interval has not ended, and counting how long it has been open so far
    /// would drag every median toward whatever happens to be in flight. Only a
    /// closed interval is a duration.
    /// </para>
    /// </summary>
    public static IEnumerable<StageInterval> Of(ApplicationTimeline timeline)
    {
        // Ordered by time rather than by the pipeline, because that is what makes
        // the pairs meaningful. They normally agree; a reopened application can
        // disagree, and taking the times at their word keeps every duration
        // non-negative without a special case for it.
        var checkpoints = Checkpoints(timeline)
            .Where(checkpoint => checkpoint.At is not null)
            .OrderBy(checkpoint => checkpoint.At!.Value)
            .ToArray();

        for (var i = 0; i < checkpoints.Length - 1; i++)
        {
            // The last checkpoint names no interval: either the application is
            // still sitting there, or it is the end of the pipeline and there is
            // nothing after it to measure to.
            if (checkpoints[i].Stage is { } stage)
            {
                yield return new StageInterval(stage, checkpoints[i + 1].At!.Value - checkpoints[i].At!.Value);
            }
        }
    }

    /// <summary>
    /// Every moment this application is known to have arrived somewhere. Closing
    /// carries no stage of its own - it ends the interval before it and starts
    /// nothing - so it is the one entry that can only ever be a boundary.
    /// </summary>
    private static IEnumerable<(string? Stage, DateTimeOffset? At)> Checkpoints(ApplicationTimeline timeline)
    {
        yield return (PipelineStages.Applied, timeline.AppliedAt);
        yield return (PipelineStages.Screening, timeline.ReachedScreeningAt);
        yield return (PipelineStages.Interview, timeline.ReachedInterviewAt);
        yield return (PipelineStages.Offer, timeline.ReachedOfferAt);
        yield return (null, timeline.ClosedAt);
    }
}
