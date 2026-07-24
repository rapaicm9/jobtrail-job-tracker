using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>What an <see cref="ActivityLogEntry"/> records.</summary>
internal enum ActivityKind
{
    /// <summary>The application was created; it entered the pipeline at <see cref="Stage.Applied"/>.</summary>
    Created,

    /// <summary>The application moved between stages - the from/to and its kind are carried.</summary>
    StageChanged,
}

/// <summary>
/// One append-only row on an application's timeline. This slice writes the
/// automatic entries - the application's creation, and (next slice) each stage
/// change - so the history is captured from the first write; manual user notes
/// join later. Construction goes through the factories so every entry is
/// internally consistent (a <see cref="ActivityKind.Created"/> entry only ever
/// carries the entry stage, a stage change always carries both ends). The row is
/// a child of its <see cref="ApplicationId"/> and is cascade-deleted with it;
/// <see cref="OwnerId"/> is carried for erasure and the owner-scoped timeline read.
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

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The entry marking an application's creation, at the <see cref="Stage.Applied"/> entry stage.</summary>
    public static ActivityLogEntry Created(Guid applicationId, UserId ownerId) => new()
    {
        ApplicationId = applicationId,
        OwnerId = ownerId,
        Kind = ActivityKind.Created,
        ToStage = Stage.Applied,
    };
}
