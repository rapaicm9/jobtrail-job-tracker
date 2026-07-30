using JobTrail.Modules.Analytics.Features.GetInsights;
using Shouldly;

namespace JobTrail.Modules.Analytics.Tests;

/// <summary>
/// How long an application spent at each stage, read off the checkpoints it left
/// behind.
/// <para>
/// The arithmetic that is easiest to get quietly wrong in this module: a skipped
/// stage, a stage still in progress, and an application that ended mid-pipeline
/// all look similar in the data and mean different things. Nothing here needs a
/// database, so the cases are enumerated rather than sampled.
/// </para>
/// </summary>
public sealed class StageIntervalTests
{
    private static readonly DateOnly Applied = new(2026, 3, 2);

    private static DateTimeOffset Day(int day) => new(2026, 3, day, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// An application that has been submitted. The applied date is not a parameter
    /// on purpose - a helper that defaulted it would make "no applied date"
    /// indistinguishable from "not specified in this test", and the one case that
    /// matters would silently test the other.
    /// </summary>
    private static ApplicationTimeline Timeline(
        DateTimeOffset? screening = null,
        DateTimeOffset? interview = null,
        DateTimeOffset? offer = null,
        DateTimeOffset? closed = null) =>
        new(Applied, null, screening, interview, offer, closed, null, null);

    /// <summary>One whose submission has not been delivered yet, so it has no applied date.</summary>
    private static ApplicationTimeline Unsubmitted(
        DateTimeOffset? screening = null,
        DateTimeOffset? interview = null,
        DateTimeOffset? offer = null,
        DateTimeOffset? closed = null) =>
        new(null, null, screening, interview, offer, closed, null, null);

    [Fact]
    public void A_straight_walk_yields_one_interval_per_stage_left()
    {
        var intervals = StageIntervals.Of(Timeline(
            screening: Day(4), interview: Day(9), offer: Day(16))).ToArray();

        intervals.Select(interval => interval.Stage).ShouldBe(["Applied", "Screening", "Interview"]);
        intervals[0].Duration.ShouldBe(TimeSpan.FromDays(2));
        intervals[1].Duration.ShouldBe(TimeSpan.FromDays(5));
        intervals[2].Duration.ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    public void The_stage_it_is_sitting_at_now_yields_nothing()
    {
        // The interval has not ended. Counting how long it has been open so far
        // would drag every median toward whatever happens to be in flight.
        var intervals = StageIntervals.Of(Timeline(screening: Day(4))).ToArray();

        intervals.ShouldHaveSingleItem().Stage.ShouldBe("Applied");
    }

    [Fact]
    public void A_skipped_stage_contributes_nothing_and_lengthens_the_one_before_it()
    {
        // Applied straight to Interview: it was never in Screening, so Screening
        // gets no sample, and the time in Applied runs all the way to the interview.
        var intervals = StageIntervals.Of(Timeline(interview: Day(12), offer: Day(15))).ToArray();

        intervals.Select(interval => interval.Stage).ShouldBe(["Applied", "Interview"]);
        intervals[0].Duration.ShouldBe(TimeSpan.FromDays(10));
        intervals[1].Duration.ShouldBe(TimeSpan.FromDays(3));
    }

    [Fact]
    public void Closing_ends_the_interval_it_interrupts_and_starts_none()
    {
        var intervals = StageIntervals.Of(Timeline(screening: Day(4), closed: Day(10))).ToArray();

        intervals.Select(interval => interval.Stage).ShouldBe(["Applied", "Screening"]);
        intervals[1].Duration.ShouldBe(TimeSpan.FromDays(6));
    }

    [Fact]
    public void An_application_rejected_without_a_reply_still_measures_its_time_applied()
    {
        var intervals = StageIntervals.Of(Timeline(closed: Day(30))).ToArray();

        intervals.ShouldHaveSingleItem().Stage.ShouldBe("Applied");
        intervals[0].Duration.ShouldBe(TimeSpan.FromDays(28));
    }

    [Fact]
    public void An_application_that_has_only_been_sent_yields_nothing()
    {
        StageIntervals.Of(Timeline()).ShouldBeEmpty();
    }

    [Fact]
    public void An_application_whose_submission_has_not_arrived_yet_yields_only_what_it_can()
    {
        // No applied date, so there is no start for the first interval - but the
        // stages it has reached still bound each other.
        var intervals = StageIntervals.Of(Unsubmitted(screening: Day(4), interview: Day(9))).ToArray();

        intervals.ShouldHaveSingleItem().Stage.ShouldBe("Screening");
        intervals[0].Duration.ShouldBe(TimeSpan.FromDays(5));
    }

    [Fact]
    public void Checkpoints_out_of_pipeline_order_never_produce_a_negative_duration()
    {
        // Reachable: the reached-at columns are merged with LEAST across events
        // that may arrive in any order, and a reopened application can walk a
        // stage twice. Ordering by time rather than by the pipeline is what keeps
        // every duration non-negative without a special case.
        var intervals = StageIntervals.Of(Timeline(
            screening: Day(20), interview: Day(6), offer: Day(11))).ToArray();

        intervals.ShouldAllBe(interval => interval.Duration >= TimeSpan.Zero);
        intervals.Select(interval => interval.Stage).ShouldBe(["Applied", "Interview", "Offer"]);
    }

    [Fact]
    public void Every_stage_reached_before_the_last_one_is_measured()
    {
        var intervals = StageIntervals.Of(Timeline(
            screening: Day(4), interview: Day(9), offer: Day(16), closed: Day(20))).ToArray();

        // Closing bounds the offer, so all four stages produce a sample.
        intervals.Select(interval => interval.Stage)
            .ShouldBe(["Applied", "Screening", "Interview", "Offer"]);
        intervals[3].Duration.ShouldBe(TimeSpan.FromDays(4));
    }
}
