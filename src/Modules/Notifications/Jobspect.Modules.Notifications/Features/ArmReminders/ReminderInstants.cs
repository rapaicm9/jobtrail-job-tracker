using Jobspect.Modules.Notifications.Domain;
using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Features.ArmReminders;

/// <summary>
/// When each reminder fires, worked out from the thing it is about and the zone
/// its owner lives in. The whole of this module's timezone arithmetic, and
/// deliberately the whole of it in one pure place: no database, no clock of its
/// own, no I/O. The caller resolves the owner's zone and reads the clock, and
/// passes both in.
/// <para>
/// That purity is the point rather than a nicety. Every case worth proving here -
/// a spring-forward, a fall-back, a booking whose local date is not its UTC date,
/// an instant that has already gone by - is a matter of arithmetic, and driving
/// them through a host and a database would make a slow test of a fast one while
/// proving less.
/// </para>
/// </summary>
internal static class ReminderInstants
{
    /// <summary>
    /// Every kind an interview round can hold, whether or not one is armed today.
    /// <para>
    /// The caller needs the whole set, not just the instants that survived the
    /// past-instant filter, because a slot whose new instant has already gone by
    /// has to be <em>retracted</em> rather than left alone. Move a round from next
    /// week to an hour from now and the morning-before moment is in the past: it is
    /// not armed again, and the row still holding last week's morning must not be
    /// left to fire for a round that has moved.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<ReminderKind> InterviewKinds =
        [ReminderKind.InterviewMorningBefore, ReminderKind.InterviewHourBefore];

    /// <summary>Both kinds a posting's deadline can hold. See <see cref="InterviewKinds"/>.</summary>
    public static readonly IReadOnlyList<ReminderKind> ApplicationDeadlineKinds =
        [ReminderKind.ApplicationDeadlineThreeDaysBefore, ReminderKind.ApplicationDeadlineMorningOf];

    /// <summary>All three kinds an offer decision can hold. See <see cref="InterviewKinds"/>.</summary>
    public static readonly IReadOnlyList<ReminderKind> OfferDecisionKinds =
        [
            ReminderKind.OfferDecisionThreeDaysBefore,
            ReminderKind.OfferDecisionDayBefore,
            ReminderKind.OfferDecisionMorningOf,
        ];

    /// <summary>
    /// "Morning", once, for every clock-based reminder here: late enough not to be
    /// missed overnight, early enough to act on. The relative ones need no clock of
    /// their own and do not use it.
    /// </summary>
    private static readonly TimeOnly Morning = new(11, 0);

    /// <summary>How far ahead of a round the second interview reminder fires.</summary>
    private static readonly TimeSpan InterviewLead = TimeSpan.FromHours(1);

    /// <summary>The long lead shared by both kinds of deadline.</summary>
    private const int LongLeadDays = 3;

    /// <summary>
    /// Stepping granularity when 11:00 does not exist locally, and a bound on how
    /// far it will step. A day is past any real gap - the largest on record is the
    /// 24 hours Pacific/Apia skipped in December 2011 - so exhausting it means the
    /// zone data is telling us something we do not understand, and the UTC fallback
    /// below is a better answer than a loop that never ends.
    /// </summary>
    private static readonly TimeSpan GapStep = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan MaxGap = TimeSpan.FromDays(1);

    /// <summary>
    /// The two reminders for an interview round: 11:00 the day before it, and an
    /// hour before it.
    /// <para>
    /// The morning-before is anchored on the round's <em>local</em> date, not its
    /// UTC one. An interview at 08:00 in Auckland is the previous day in UTC, and
    /// counting back a day from the UTC date would put the reminder two local days
    /// early - which reads as a bug to the person receiving it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ReminderInstant> ForInterview(
        DateTimeOffset scheduledAt, string? ianaTimeZoneId, DateTimeOffset now)
    {
        var zone = Resolve(ianaTimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scheduledAt, zone).DateTime);

        return Future(
            now,
            new ReminderInstant(ReminderKind.InterviewMorningBefore, MorningOn(localDate.AddDays(-1), zone)),
            new ReminderInstant(ReminderKind.InterviewHourBefore, scheduledAt - InterviewLead));
    }

    /// <summary>
    /// The two reminders for a posting's deadline: 11:00 three days before, and
    /// 11:00 on the day itself.
    /// </summary>
    public static IReadOnlyList<ReminderInstant> ForApplicationDeadline(
        DateOnly deadline, string? ianaTimeZoneId, DateTimeOffset now)
    {
        var zone = Resolve(ianaTimeZoneId);

        return Future(
            now,
            new ReminderInstant(
                ReminderKind.ApplicationDeadlineThreeDaysBefore,
                MorningOn(deadline.AddDays(-LongLeadDays), zone)),
            new ReminderInstant(ReminderKind.ApplicationDeadlineMorningOf, MorningOn(deadline, zone)));
    }

    /// <summary>
    /// The three reminders for an offer decision: 11:00 three days before, 11:00
    /// the day before, and 11:00 on the day. One more than a posting's deadline
    /// gets, because there is a job at the end of this one.
    /// </summary>
    public static IReadOnlyList<ReminderInstant> ForOfferDecision(
        DateOnly deadline, string? ianaTimeZoneId, DateTimeOffset now)
    {
        var zone = Resolve(ianaTimeZoneId);

        return Future(
            now,
            new ReminderInstant(
                ReminderKind.OfferDecisionThreeDaysBefore,
                MorningOn(deadline.AddDays(-LongLeadDays), zone)),
            new ReminderInstant(ReminderKind.OfferDecisionDayBefore, MorningOn(deadline.AddDays(-1), zone)),
            new ReminderInstant(ReminderKind.OfferDecisionMorningOf, MorningOn(deadline, zone)));
    }

    /// <summary>
    /// The next 11:00 in the owner's own timezone, strictly after <paramref name="now"/>.
    /// <para>
    /// The follow-up's instant, and the only one here computed from <em>when it was
    /// noticed</em> rather than from a date the owner typed. Every other reminder is
    /// armed ahead of a moment that is already known; a silence has no such moment,
    /// so the scan that discovers one arms it for the next morning there is.
    /// </para>
    /// <para>
    /// <b>Strictly after, which is what makes the past-instant rule hold by
    /// construction here.</b> The other kinds are filtered against the clock after
    /// the fact, because their instants can be anywhere; this one cannot be armed
    /// into the past at all. A scan running at 14:00 local finds today's 11:00 gone
    /// and takes tomorrow's, rather than raising something the sweep would fire
    /// immediately.
    /// </para>
    /// </summary>
    public static DateTimeOffset NextMorningAfter(DateTimeOffset now, string? ianaTimeZoneId)
    {
        var zone = Resolve(ianaTimeZoneId);
        var today = LocalDate.TodayIn(now, ianaTimeZoneId);
        var morning = MorningOn(today, zone);

        // At exactly 11:00 the morning has arrived rather than being ahead, so the
        // nudge goes to tomorrow. A day late beats a reminder that is due the
        // instant it exists.
        return morning > now ? morning : MorningOn(today.AddDays(1), zone);
    }

    /// <summary>
    /// The past-instant rule, applied here so the writer is never asked to arm one.
    /// <para>
    /// Book an interview for two hours from now and the morning-before moment is
    /// long gone; recording it would only give the sweep something to fire
    /// immediately, which reads as a bug rather than a courtesy. The same rule
    /// quietly handles a deadline set for tomorrow - the three-days-before instant
    /// is dropped and the morning-of one is kept - so no caller needs a special
    /// case for it.
    /// </para>
    /// <para>
    /// Strictly after: an instant landing exactly on the clock reading has arrived,
    /// and arming it would be arming something already due.
    /// </para>
    /// </summary>
    private static ReminderInstant[] Future(DateTimeOffset now, params ReminderInstant[] instants) =>
        [.. instants.Where(instant => instant.DueAt > now)];

    /// <summary>
    /// 11:00 on a given local date, as the UTC instant it corresponds to.
    /// <para>
    /// <b>Internal rather than private so its own tests can reach it with a zone
    /// they built.</b> Both edges below are unreachable through real zone data:
    /// a sweep of every zone this runtime knows, over 1970-2040, finds no date on
    /// which 11:00 is an invalid local time - daylight gaps sit in the small hours,
    /// and the famous skipped days (Pacific/Apia in 2011) are changes of base offset
    /// rather than of daylight saving, which is not what <c>IsInvalidTime</c>
    /// reports. Driving the branch through a constructed zone is the only way to
    /// prove it works rather than merely assume it never runs.
    /// </para>
    /// <para>
    /// Two edges of the zone database meet here, and they are not symmetrical.
    /// An <em>ambiguous</em> local time - the hour a fall-back repeats - needs no
    /// handling: the conversion resolves it to standard time, which is a definite
    /// answer and the later of the two. An <em>invalid</em> one - an hour a
    /// spring-forward skipped - would throw, and an exception here is not a local
    /// problem: it would leave the event to be retried until the outbox abandons
    /// it, losing the reminder silently. So a missing 11:00 steps forward to the
    /// first local time that exists, which is 12:00 after an ordinary one-hour
    /// shift. The owner is reminded slightly later rather than not at all.
    /// </para>
    /// </summary>
    internal static DateTimeOffset MorningOn(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(Morning, DateTimeKind.Unspecified);
        var stepped = TimeSpan.Zero;

        while (zone.IsInvalidTime(local) && stepped < MaxGap)
        {
            local = local.Add(GapStep);
            stepped += GapStep;
        }

        // Past any gap the zone database plausibly holds. Rather than throw on
        // data we do not understand, fall back the way an unresolvable zone id
        // already does - a reminder an hour out beats no reminder at all.
        if (zone.IsInvalidTime(local))
        {
            return new DateTimeOffset(date.ToDateTime(Morning, DateTimeKind.Utc));
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// The owner's zone, or UTC when it is absent or the host cannot resolve it.
    /// <para>
    /// The lenient half of the rule, and the same one <c>LocalDate.TodayIn</c>
    /// applies for the same reason: a user whose zone has been renamed out from
    /// under us still gets their reminders, off by at most the offset, rather than
    /// an event that fails every time it is delivered. The stored id is validated
    /// when it is set, so this path is for a zone database that has moved on.
    /// </para>
    /// </summary>
    private static TimeZoneInfo Resolve(string? ianaTimeZoneId) =>
        ianaTimeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(ianaTimeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
}
