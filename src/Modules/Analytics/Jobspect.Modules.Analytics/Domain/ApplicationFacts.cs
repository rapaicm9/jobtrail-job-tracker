using Jobspect.SharedKernel;

namespace Jobspect.Modules.Analytics.Domain;

/// <summary>
/// What this module knows about one application, assembled from the events the
/// Applications module publishes. One row per application, and every figure the
/// dashboard shows is an aggregation over these rows - there are no counters.
/// <para>
/// It is this module's own record rather than a copy of another module's table:
/// it holds what was announced, in the shape the metrics want. Nothing here is a
/// foreign key. The application, owner, campaign and company ids all belong to
/// another schema, and a cross-schema foreign key is exactly the boundary this
/// module exists on the far side of.
/// </para>
/// <para>
/// <b>These rows are the system of record.</b> The outbox prunes what it has
/// delivered, so there is no stream to replay: a figure this row cannot answer is
/// one nobody can recover, and a column added later can only ever be filled for
/// applications seen after it was added.
/// </para>
/// <para>
/// The properties fall into three groups, and which group a property is in
/// decides how a projection is allowed to write it - see the watermarks below.
/// </para>
/// </summary>
internal sealed class ApplicationFacts
{
    /// <summary>
    /// The application this row is about, and the key every projection upserts on -
    /// which is what makes redelivery idempotent by construction rather than by
    /// arithmetic. Supplied by the event, never generated here, so unlike every
    /// other table in this system it carries no <c>uuidv7()</c> default. It is
    /// still a UUIDv7, so the rows order by the application's creation time.
    /// </summary>
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The account the application belongs to. Every event carries it, so it is
    /// known however this row came to exist. A non-FK reference to an Identity
    /// account, and the column erasure works from.
    /// </summary>
    public UserId OwnerId { get; set; }

    // ---------------------------------------------------------------------
    // Write-once dimensions. Only ApplicationSubmitted carries these, and it is
    // published once per application, so a redelivery rewrites identical values
    // and needs no guard of any kind.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The campaign the application currently sits in.
    /// <para>
    /// Nullable, which looks wrong - every application has a campaign - and is a
    /// direct consequence of unordered delivery. Only <c>ApplicationSubmitted</c>
    /// carries the campaign, so an application whose stage change is delivered
    /// first exists here before its campaign is known. Refusing to insert that row
    /// would drop the stage change, which is the loss the read model is built to
    /// avoid; a briefly incomplete row that heals is the cheaper failure.
    /// </para>
    /// <para>
    /// Written by two events, so it is guarded - see <see cref="CampaignRecordedAt"/>.
    /// </para>
    /// </summary>
    public Guid? CampaignId { get; set; }

    /// <summary>
    /// The company applied to, if the user attached one. Carried by
    /// <c>ApplicationSubmitted</c> and kept even though no figure breaks down by it
    /// yet: the events offer it now, and there is no stream to recover it from
    /// later.
    /// </summary>
    public Guid? CompanyId { get; set; }

    /// <summary>
    /// The date the user says they applied - the anchor every time-based metric
    /// measures from, and the axis of the applications-per-week trend. The user's
    /// own date rather than when they recorded it, so an application entered a week
    /// late still measures from when it actually happened.
    /// <para>
    /// Nullable for the same reason as <see cref="CampaignId"/>.
    /// </para>
    /// </summary>
    public DateOnly? AppliedDate { get; set; }

    /// <summary>Where the application came from; a Pro breakdown.</summary>
    public string? Source { get; set; }

    /// <summary>Onsite / hybrid / remote, as text; a Pro breakdown.</summary>
    public string? WorkMode { get; set; }

    // ---------------------------------------------------------------------
    // Latest-wins facts. Several events write each of these, so an older write
    // must not overwrite a newer one - each group is guarded by a watermark.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Where the application sits now, as the text the event carried.
    /// <para>
    /// Deliberately not an enum of this module's own. The pipeline belongs to the
    /// Applications module, whose <c>Stage</c> is internal to it; mirroring it here
    /// would mean this module asserting knowledge of a pipeline it does not own,
    /// and a stage name it did not recognise would throw on the delivery path.
    /// Stored as it arrived, an unknown stage is simply reported as itself. The
    /// funnel does not read this column at all - it reads the reached-at timestamps
    /// below - so nothing downstream depends on knowing the full set.
    /// </para>
    /// </summary>
    public string? Stage { get; set; }

    /// <summary>
    /// The outcome the application ended on, once it has ended. Cleared when a
    /// closed application is reopened.
    /// </summary>
    public string? Outcome { get; set; }

    /// <summary>
    /// When the application entered <see cref="Stage"/>. Closes the last interval
    /// for time-in-stage, the one the reached-at timestamps cannot bound because
    /// nothing has followed it yet.
    /// </summary>
    public DateTimeOffset? StageEnteredAt { get; set; }

    /// <summary>When the application closed; null while it is live, and cleared on reopen.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// The occurrence time of the newest event to have written <see cref="Stage"/>,
    /// <see cref="Outcome"/>, <see cref="StageEnteredAt"/> or <see cref="ClosedAt"/>.
    /// A projection applies its write only when its own event is newer than this.
    /// <para>
    /// Per fact group rather than per row, and that distinction is load-bearing. A
    /// single row-level watermark would let a late-arriving interview reject a
    /// stage change outright, discarding a fact no other event carries. Guarding
    /// each group separately means an out-of-order event loses only the facts it
    /// shares with a newer one.
    /// </para>
    /// </summary>
    public DateTimeOffset? StageRecordedAt { get; set; }

    /// <summary>
    /// The occurrence time of the newest event to have written
    /// <see cref="CampaignId"/> - the initial attribution on
    /// <c>ApplicationSubmitted</c>, or any later move. Both carry the whole fact, so
    /// either applies without having seen the other; this is what decides which one
    /// stands when they arrive back to front.
    /// </summary>
    public DateTimeOffset? CampaignRecordedAt { get; set; }

    // ---------------------------------------------------------------------
    // Monotone facts: the earliest time each thing happened, and it never
    // happens for the first time twice. Written with LEAST, which makes them
    // commutative and idempotent - so unlike the group above they need no
    // watermark, and neither redelivery nor arrival order can disturb them.
    // ---------------------------------------------------------------------

    /// <summary>
    /// When the employer first came back - the basis of the response rate and of
    /// time-to-first-response.
    /// <para>
    /// Not simply the first move off Applied: being ghosted is the <em>absence</em>
    /// of a response, and withdrawing is the user's own act. Counting either would
    /// inflate the rate with the applications that most clearly did not get an
    /// answer.
    /// </para>
    /// </summary>
    public DateTimeOffset? FirstResponseAt { get; set; }

    /// <summary>When the application first reached Screening, if it ever did.</summary>
    public DateTimeOffset? ReachedScreeningAt { get; set; }

    /// <summary>When the application first reached Interview, if it ever did.</summary>
    public DateTimeOffset? ReachedInterviewAt { get; set; }

    /// <summary>
    /// When the application first reached Offer, if it ever did. Also what
    /// time-to-offer measures to.
    /// <para>
    /// These three exist as columns rather than being read off <see cref="Stage"/>
    /// because a forward move may skip: an application that went straight from
    /// Applied to Offer never had an interview, and one that was rejected out of
    /// Screening shows a terminal stage that says nothing about how far it got. A
    /// funnel built on the current stage would be wrong in both directions.
    /// </para>
    /// <para>
    /// They pay for time-in-stage as well: the next stage's entry is this stage's
    /// exit, so the durations fall out of the same four timestamps. Both metrics
    /// describe the first pass through the pipeline - a reopened application that
    /// walks a stage twice is measured on the first walk.
    /// </para>
    /// </summary>
    public DateTimeOffset? ReachedOfferAt { get; set; }

    /// <summary>
    /// When the first interview round was put on the calendar - how long it took to
    /// get an interview booked, which is not the same as reaching the Interview
    /// stage.
    /// </summary>
    public DateTimeOffset? FirstInterviewScheduledAt { get; set; }

    /// <summary>
    /// When this row first appeared. Operational only - for answering "when did
    /// this show up" while debugging a projection - and <b>never</b> an input to a
    /// figure. A row records when each fact was true, not when it was received, and
    /// a metric computed from arrival order would be wrong.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
