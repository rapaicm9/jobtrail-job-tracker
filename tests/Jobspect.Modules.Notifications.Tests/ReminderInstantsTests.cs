using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Shouldly;

namespace Jobspect.Modules.Notifications.Tests;

/// <summary>
/// When each reminder fires. All arithmetic - a date, a zone and a clock reading -
/// so it is driven directly, with no host and no database.
/// <para>
/// The zone ids are real ones and the expected instants were taken from the runtime
/// rather than worked out by hand, which is the only honest way to write this: an
/// expectation derived from the same reasoning as the code proves nothing about
/// either.
/// </para>
/// </summary>
public sealed class ReminderInstantsTests
{
    private const string NoDaylightSaving = "Asia/Kolkata";      // UTC+5:30 all year
    private const string Northern = "Europe/Belgrade";           // CET / CEST
    private const string Southern = "Pacific/Auckland";          // NZST / NZDT

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ the morning

    [Fact]
    public void Morning_is_eleven_where_the_owner_is()
    {
        var morning = ReminderInstants.ForApplicationDeadline(
            new DateOnly(2026, 6, 15), NoDaylightSaving, Utc(2026, 1, 1));

        // 11:00 at UTC+5:30.
        Instant(morning, ReminderKind.ApplicationDeadlineMorningOf).ShouldBe(Utc(2026, 6, 15, 5, 30));
    }

    [Theory]
    // Northern hemisphere, either side of the spring-forward on 29 March 2026:
    // 11:00 costs an hour less of UTC once the clocks have gone forward.
    [InlineData(2026, 3, 28, 10)]
    [InlineData(2026, 3, 29, 9)]
    // And either side of the fall-back on 25 October 2026, the other way.
    [InlineData(2026, 10, 24, 9)]
    [InlineData(2026, 10, 25, 10)]
    public void Morning_follows_the_offset_across_a_daylight_transition(
        int year, int month, int day, int expectedUtcHour)
    {
        var deadline = new DateOnly(year, month, day);

        var morning = ReminderInstants.ForApplicationDeadline(deadline, Northern, Utc(2026, 1, 1));

        Instant(morning, ReminderKind.ApplicationDeadlineMorningOf)
            .ShouldBe(Utc(year, month, day, expectedUtcHour));
    }

    [Theory]
    // A southern zone runs the transitions the opposite way round, and is far
    // enough east that its morning is the previous day in UTC.
    [InlineData(2026, 1, 15, 2026, 1, 14, 22)]  // NZDT, UTC+13
    [InlineData(2026, 7, 15, 2026, 7, 14, 23)]  // NZST, UTC+12
    public void Morning_in_a_southern_zone_can_fall_on_the_previous_utc_day(
        int year, int month, int day, int utcYear, int utcMonth, int utcDay, int utcHour)
    {
        var morning = ReminderInstants.ForApplicationDeadline(
            new DateOnly(year, month, day), Southern, Utc(2025, 1, 1));

        Instant(morning, ReminderKind.ApplicationDeadlineMorningOf)
            .ShouldBe(Utc(utcYear, utcMonth, utcDay, utcHour));
    }

    [Fact]
    public void An_unresolvable_zone_falls_back_to_utc()
    {
        var morning = ReminderInstants.ForApplicationDeadline(
            new DateOnly(2026, 6, 15), "Mars/Olympus_Mons", Utc(2026, 1, 1));

        Instant(morning, ReminderKind.ApplicationDeadlineMorningOf).ShouldBe(Utc(2026, 6, 15, 11));
    }

    [Fact]
    public void An_absent_zone_falls_back_to_utc()
    {
        var morning = ReminderInstants.ForApplicationDeadline(
            new DateOnly(2026, 6, 15), ianaTimeZoneId: null, Utc(2026, 1, 1));

        Instant(morning, ReminderKind.ApplicationDeadlineMorningOf).ShouldBe(Utc(2026, 6, 15, 11));
    }

    // --------------------------------------------- the two edges of a zone file

    /// <summary>
    /// No real zone puts 11:00 inside a daylight gap - a sweep of every zone this
    /// runtime knows over 1970-2040 finds none - so the only way to prove the
    /// step-forward works is to build a zone that does. This one springs forward at
    /// 10:30, which makes 10:30-11:30 local a time that never happens.
    /// </summary>
    [Fact]
    public void A_morning_inside_a_daylight_gap_steps_to_the_first_time_that_exists()
    {
        var gapped = ZoneSpringingForwardAt(new TimeOnly(10, 30));

        var morning = ReminderInstants.MorningOn(new DateOnly(2026, 6, 1), gapped);

        // 11:00 does not exist; 11:30 is the first that does, at the new +1 offset.
        morning.ShouldBe(Utc(2026, 6, 1, 10, 30));
    }

    /// <summary>
    /// The other edge, and the one that needs no handling: a repeated hour resolves
    /// to standard time. Asserted rather than assumed, because "throws on invalid,
    /// resolves ambiguous silently" is exactly the asymmetry that would otherwise be
    /// remembered backwards.
    /// </summary>
    [Fact]
    public void An_ambiguous_morning_resolves_to_standard_time()
    {
        var overlapping = ZoneSpringingForwardAt(new TimeOnly(11, 30));

        // The autumn transition of the same zone puts 10:00-11:00 twice on 1 Nov.
        var morning = ReminderInstants.MorningOn(new DateOnly(2026, 11, 1), overlapping);

        // Standard time is the later of the two readings: +0 rather than +1.
        morning.ShouldBe(Utc(2026, 11, 1, 11));
    }

    // -------------------------------------------------------------- interviews

    [Fact]
    public void An_interview_is_announced_the_morning_before_and_an_hour_before()
    {
        // 12:00 local in Belgrade on 15 June (CEST, UTC+2).
        var scheduled = Utc(2026, 6, 15, 10);

        var instants = ReminderInstants.ForInterview(scheduled, Northern, Utc(2026, 6, 1));

        instants.Select(instant => instant.Kind).ShouldBe(
            [ReminderKind.InterviewMorningBefore, ReminderKind.InterviewHourBefore]);

        Instant(instants, ReminderKind.InterviewMorningBefore).ShouldBe(Utc(2026, 6, 14, 9));
        Instant(instants, ReminderKind.InterviewHourBefore).ShouldBe(Utc(2026, 6, 15, 9));
    }

    /// <summary>
    /// The morning-before counts back from the round's <em>local</em> date. This
    /// booking is 15 June in Auckland and 14 June in UTC, so counting back from the
    /// UTC date would announce it two local days early.
    /// </summary>
    [Fact]
    public void The_morning_before_follows_the_rounds_local_date_not_its_utc_one()
    {
        // 08:00 on 15 June in Auckland (NZST, UTC+12) is 20:00 on 14 June in UTC.
        var scheduled = Utc(2026, 6, 14, 20);

        var instants = ReminderInstants.ForInterview(scheduled, Southern, Utc(2026, 6, 1));

        // 11:00 on 14 June in Auckland - the day before the round locally.
        Instant(instants, ReminderKind.InterviewMorningBefore).ShouldBe(Utc(2026, 6, 13, 23));
    }

    [Fact]
    public void A_round_whose_morning_has_gone_by_keeps_only_the_hour_before()
    {
        var scheduled = Utc(2026, 6, 15, 10);

        // After the morning-before instant (14 June 09:00Z), before the hour-before.
        var instants = ReminderInstants.ForInterview(scheduled, Northern, Utc(2026, 6, 14, 12));

        instants.Select(instant => instant.Kind).ShouldBe([ReminderKind.InterviewHourBefore]);
    }

    [Fact]
    public void A_round_starting_within_the_hour_is_not_announced_at_all()
    {
        var scheduled = Utc(2026, 6, 15, 10);

        var instants = ReminderInstants.ForInterview(scheduled, Northern, Utc(2026, 6, 15, 9, 30));

        instants.ShouldBeEmpty();
    }

    // -------------------------------------------------------------- deadlines

    [Fact]
    public void A_posting_deadline_is_announced_twice()
    {
        var instants = ReminderInstants.ForApplicationDeadline(
            new DateOnly(2026, 6, 15), Northern, Utc(2026, 6, 1));

        instants.Select(instant => instant.Kind).ShouldBe(
            [
                ReminderKind.ApplicationDeadlineThreeDaysBefore,
                ReminderKind.ApplicationDeadlineMorningOf,
            ]);

        Instant(instants, ReminderKind.ApplicationDeadlineThreeDaysBefore).ShouldBe(Utc(2026, 6, 12, 9));
        Instant(instants, ReminderKind.ApplicationDeadlineMorningOf).ShouldBe(Utc(2026, 6, 15, 9));
    }

    /// <summary>
    /// The case ADR 0006 names: a deadline set closer than its own longest lead
    /// keeps the reminders that are still ahead and silently drops the rest. No
    /// caller needs a special branch for it.
    /// </summary>
    [Fact]
    public void A_deadline_set_inside_its_own_lead_keeps_only_what_is_still_ahead()
    {
        var instants = ReminderInstants.ForApplicationDeadline(
            new DateOnly(2026, 6, 15), Northern, Utc(2026, 6, 13));

        instants.Select(instant => instant.Kind).ShouldBe([ReminderKind.ApplicationDeadlineMorningOf]);
    }

    [Fact]
    public void An_offer_decision_is_announced_three_times()
    {
        var instants = ReminderInstants.ForOfferDecision(
            new DateOnly(2026, 6, 15), Northern, Utc(2026, 6, 1));

        instants.Select(instant => instant.Kind).ShouldBe(
            [
                ReminderKind.OfferDecisionThreeDaysBefore,
                ReminderKind.OfferDecisionDayBefore,
                ReminderKind.OfferDecisionMorningOf,
            ]);

        Instant(instants, ReminderKind.OfferDecisionDayBefore).ShouldBe(Utc(2026, 6, 14, 9));
    }

    [Fact]
    public void A_deadline_wholly_in_the_past_is_not_announced_at_all()
    {
        var instants = ReminderInstants.ForOfferDecision(
            new DateOnly(2026, 6, 15), Northern, Utc(2026, 7, 1));

        instants.ShouldBeEmpty();
    }

    /// <summary>
    /// The boundary is strict. An instant landing exactly on the clock reading has
    /// arrived, and arming it would be arming something already due - which the
    /// sweep would then have to decide whether to fire or drop as late.
    /// </summary>
    [Fact]
    public void An_instant_falling_exactly_now_is_not_armed()
    {
        var morningOf = Utc(2026, 6, 15, 9);

        ReminderInstants.ForApplicationDeadline(new DateOnly(2026, 6, 15), Northern, morningOf)
            .ShouldBeEmpty();

        // A second earlier and it is still ahead of us.
        ReminderInstants.ForApplicationDeadline(
                new DateOnly(2026, 6, 15), Northern, morningOf.AddSeconds(-1))
            .Select(instant => instant.Kind)
            .ShouldBe([ReminderKind.ApplicationDeadlineMorningOf]);
    }

    // ----------------------------------------------------------------- helpers

    private static DateTimeOffset Instant(IReadOnlyList<ReminderInstant> instants, ReminderKind kind) =>
        instants.Single(instant => instant.Kind == kind).DueAt;

    /// <summary>
    /// A zone on UTC that goes an hour forward at <paramref name="springsForwardAt"/>
    /// on 1 June and back at the same reading on 1 November - so the hour after that
    /// time does not exist in June, and happens twice in November.
    /// </summary>
    private static TimeZoneInfo ZoneSpringingForwardAt(TimeOnly springsForwardAt)
    {
        var transition = new DateTime(1, 1, 1).Add(springsForwardAt.ToTimeSpan());

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            dateStart: new DateTime(2026, 1, 1),
            dateEnd: new DateTime(2026, 12, 31),
            daylightDelta: TimeSpan.FromHours(1),
            daylightTransitionStart: TimeZoneInfo.TransitionTime.CreateFixedDateRule(transition, 6, 1),
            daylightTransitionEnd: TimeZoneInfo.TransitionTime.CreateFixedDateRule(transition, 11, 1));

        return TimeZoneInfo.CreateCustomTimeZone(
            $"Test/Gap{springsForwardAt:HHmm}",
            TimeSpan.Zero,
            "Test zone",
            "Test zone standard",
            "Test zone daylight",
            [rule]);
    }
}
