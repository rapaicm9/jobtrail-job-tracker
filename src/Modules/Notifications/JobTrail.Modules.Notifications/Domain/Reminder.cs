using JobTrail.SharedKernel;

namespace JobTrail.Modules.Notifications.Domain;

/// <summary>
/// One moment at which this module has undertaken to tell somebody something.
/// <para>
/// <b>The row is the whole reminder.</b> There is no scheduler entry beside it and
/// nothing to reconcile: the sweep finds due rows, retraction is a column update,
/// and erasure is a delete. A scheduler holding a trigger per reminder would be a
/// second durable record of the same fact, written in a different transaction, and
/// cancellation is exactly where the two would drift.
/// </para>
/// <para>
/// <b>A row is one armed instant, not a standing slot.</b> Rearming - a round moved
/// to a new day, a deadline pushed back - retires the row that was armed and
/// inserts another. Nothing that has already reached the owner is ever rewritten,
/// which is what lets the feed be a record of what they were actually told: an
/// interview alert that fired on Monday stays true after the interview moves to
/// Friday, and the Friday alert is a second row rather than the first one edited.
/// </para>
/// <para>
/// Nothing here is a foreign key except <see cref="RuleId"/>. The application, the
/// interview and the owner all live in another module's schema, and a cross-schema
/// foreign key is the boundary this module sits on the far side of.
/// </para>
/// </summary>
internal sealed class Reminder
{
    /// <summary>
    /// Minted here, unlike the rows this module projects from events: a reminder is
    /// this module's own decision, not a copy of somebody else's record, and the
    /// feed needs a stable id to dismiss by.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The account to be told. A non-FK reference to an Identity account, the
    /// column the feed is scoped by, and the one erasure works from.
    /// </summary>
    public UserId OwnerId { get; set; }

    /// <summary>What this reminder is about and how far ahead of it fires.</summary>
    public ReminderKind Kind { get; set; }

    /// <summary>
    /// Where it is in its life. Defaulted to <see cref="ReminderState.Pending"/> at
    /// the database, so a row cannot be inserted without one.
    /// </summary>
    public ReminderState State { get; set; }

    /// <summary>
    /// When it fires, in UTC, computed once from the owner's stored IANA zone. The
    /// instant is fixed at arming time rather than resolved at delivery: a sweep
    /// comparing stored UTC against the clock is DST-safe by construction, whereas
    /// one converting on every pass would have to be right about a zone whose rules
    /// may have changed since.
    /// </summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>
    /// The application this concerns - present on every kind, including the
    /// interview ones. It is what a retraction addresses when an application is
    /// answered or closed ("everything still pending for this application"), and
    /// what the client deep-links to from the feed.
    /// </summary>
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The interview round, on the two interview kinds only; null on the rest.
    /// Together with <see cref="ApplicationId"/> and <see cref="Kind"/> it forms the
    /// slot a reminder occupies, so a round that is rescheduled replaces its own
    /// alerts instead of accumulating them.
    /// </summary>
    public Guid? InterviewId { get; set; }

    /// <summary>
    /// The rule that raised this, on follow-ups only. The one real foreign key here,
    /// and it nulls out rather than cascades: deleting a rule stops it raising more
    /// follow-ups, and does not rewrite what the owner was already told.
    /// </summary>
    public Guid? RuleId { get; set; }

    /// <summary>
    /// The instant this reminder is <em>about</em> - the interview's own start time.
    /// Null on the kinds whose subject is a date rather than a moment.
    /// <para>
    /// Kept because the events carry it now and nothing can recover it later: this
    /// module cannot read the Applications module's tables, and the outbox prunes
    /// what it has delivered. Without it the feed can say a reminder is about an
    /// interview but not when the interview is.
    /// </para>
    /// </summary>
    public DateTimeOffset? SubjectAt { get; set; }

    /// <summary>
    /// The date this reminder is about - the posting deadline, or the day an offer
    /// has to be answered. Null on the interview kinds.
    /// <para>
    /// A separate column from <see cref="SubjectAt"/> rather than a timestamp
    /// standing in for a date. A deadline is a day, not a moment, and storing one as
    /// the other would mean inventing a time and a zone to read it back in.
    /// </para>
    /// </summary>
    public DateOnly? SubjectDate { get; set; }

    /// <summary>
    /// The occurrence time of the newest event to have decided anything about this
    /// slot - the one that armed it, or the one that retracted it.
    /// <para>
    /// Delivery is at-least-once and unordered, so an event older than this changes
    /// nothing. It is read from the newest row for the slot <em>in any state</em>,
    /// not merely a pending one: a redelivered "interview scheduled" arriving after
    /// the cancellation that retired it would otherwise find an empty slot and arm a
    /// reminder for a round that is not happening.
    /// </para>
    /// <para>
    /// The comparison is <c>&gt;=</c>, because events published in one transaction
    /// share an instant - a stage change and the closure it amounts to arrive
    /// separately, saying the same thing about the same reminders.
    /// </para>
    /// </summary>
    public DateTimeOffset SourceRecordedAt { get; set; }

    /// <summary>
    /// When this row was written. Operational only, for answering "when was this
    /// armed" while reading a feed that surprised somebody - never an input to when
    /// it fires.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
