using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Domain;

/// <summary>
/// An application this module is watching for silence, assembled from the events
/// the Applications module publishes. It exists for one question, asked on a
/// schedule: which of this account's applications have sat in Applied long enough
/// to be worth a nudge?
/// <para>
/// The scan cannot answer that by reading the Applications module's tables, and it
/// runs in the worker, where asking that module directly would mean composing it
/// into a second process - along with the outbox dispatcher that comes with it. So
/// this module keeps the little it needs: who, since when, and whether anything has
/// happened.
/// </para>
/// <para>
/// It is deliberately smaller than the read model Analytics keeps from the same
/// events. Nothing here is reported to anyone; it is scheduling input, and a column
/// that no scan reads is a column that would go stale unnoticed.
/// </para>
/// </summary>
internal sealed class TrackedApplication
{
    /// <summary>
    /// The application this is about, and the key every projection upserts on -
    /// which is what makes redelivery idempotent by construction. It arrives on the
    /// event and is never minted here, so unlike the rows this module raises itself
    /// it carries no <c>uuidv7()</c> default: a key the database rewrote would
    /// insert a second row for the same application every time an event repeated.
    /// </summary>
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The account it belongs to. Every event carries it, so it is known however
    /// this row came to exist; the scan starts from the accounts that have a rule,
    /// and erasure works from the same column.
    /// </summary>
    public UserId OwnerId { get; set; }

    /// <summary>
    /// The date the user says they applied - the anchor the follow-up counts from,
    /// and their own truth about when the wait started rather than when they
    /// recorded it.
    /// <para>
    /// Nullable, which looks wrong until delivery order is taken into account: only
    /// <c>ApplicationSubmitted</c> carries this date, so an application whose stage
    /// change is delivered first exists here before the date is known. Refusing that
    /// row would drop the stage change, and this table would then believe an
    /// application was still waiting for an answer it had already had.
    /// </para>
    /// </summary>
    public DateOnly? AppliedDate { get; set; }

    /// <summary>
    /// Where the application sits now, as the text the event carried - the pipeline
    /// belongs to the Applications module, and a stage name this one did not
    /// recognise would throw on the delivery path rather than simply being a stage
    /// it does not act on.
    /// <para>
    /// Null means nothing has said it moved, which is the ordinary case: most
    /// applications never leave Applied. The scan reads a null and a recorded
    /// "Applied" the same way, rather than this module asserting where an
    /// application starts - that is the Applications module's knowledge, and the
    /// event that would state it does not carry a stage.
    /// </para>
    /// </summary>
    public string? Stage { get; set; }

    /// <summary>
    /// The occurrence time of the newest event to have written <see cref="Stage"/>.
    /// An older one changes nothing, or an out-of-order delivery would put an
    /// answered application back into the pool of ones waiting for an answer.
    /// </summary>
    public DateTimeOffset? StageRecordedAt { get; set; }

    /// <summary>When this row first appeared. Operational only, never an input to the scan.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
