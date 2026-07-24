using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>What an <see cref="ActivityLogEntry"/> records.</summary>
internal enum ActivityKind
{
    /// <summary>The application was created; it entered the pipeline at <see cref="Stage.Applied"/>.</summary>
    Created,

    /// <summary>The application moved between stages - the from/to and its kind are carried.</summary>
    StageChanged,

    /// <summary>The user wrote a note against the application; the text is carried.</summary>
    Note,
}

/// <summary>
/// One append-only row on an application's timeline: the automatic entries - the
/// application's creation and each stage change - alongside the notes the user
/// writes by hand. Construction goes through the factories so every entry is
/// internally consistent (a <see cref="ActivityKind.Created"/> entry only ever
/// carries the entry stage, a stage change always carries both ends, a note only
/// its text). The row is a child of its <see cref="ApplicationId"/> and is
/// cascade-deleted with it; <see cref="OwnerId"/> is carried for erasure and the
/// owner-scoped timeline read.
/// </summary>
internal sealed class ActivityLogEntry
{
    private ActivityLogEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid ApplicationId { get; private set; }

    public UserId OwnerId { get; private set; }

    public ActivityKind Kind { get; private set; }

    /// <summary>The stage moved from; null for a <see cref="ActivityKind.Created"/> entry.</summary>
    public Stage? FromStage { get; private set; }

    /// <summary>The stage moved to (or entered at, on creation).</summary>
    public Stage? ToStage { get; private set; }

    /// <summary>How a stage change related to the pipeline; null for a creation entry.</summary>
    public TransitionKind? TransitionKind { get; private set; }

    /// <summary>
    /// What the user wrote; null on the automatic entries. Free text the user
    /// typed about their own job search - it stays out of logs and events.
    /// </summary>
    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The entry marking an application's creation, at the <see cref="Stage.Applied"/> entry stage.</summary>
    public static ActivityLogEntry Created(Guid applicationId, UserId ownerId) => new()
    {
        ApplicationId = applicationId,
        OwnerId = ownerId,
        Kind = ActivityKind.Created,
        ToStage = Stage.Applied,
    };

    /// <summary>
    /// The entry marking a stage change, carrying both ends and its kind straight
    /// from the accepted <see cref="StageTransition"/> - so the timeline records
    /// not just that the application moved, but how (an advance, a reopen, …).
    /// </summary>
    public static ActivityLogEntry ForStageChange(Guid applicationId, UserId ownerId, StageTransition transition) => new()
    {
        ApplicationId = applicationId,
        OwnerId = ownerId,
        Kind = ActivityKind.StageChanged,
        FromStage = transition.From,
        ToStage = transition.To,
        TransitionKind = transition.Kind,
    };

    /// <summary>
    /// A note the user wrote against the application - the one entry a client
    /// creates directly, carrying no stages because nothing moved. Named for what
    /// it builds rather than <c>Note</c>, which the text itself already takes.
    /// </summary>
    public static ActivityLogEntry ForNote(Guid applicationId, UserId ownerId, string note) => new()
    {
        ApplicationId = applicationId,
        OwnerId = ownerId,
        Kind = ActivityKind.Note,
        Note = note,
    };
}
