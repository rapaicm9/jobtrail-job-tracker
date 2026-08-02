using Jobspect.SharedKernel;

namespace Jobspect.Modules.Applications.Domain;

/// <summary>
/// A field the user has defined for themselves, which then appears on every one of
/// their applications. Account-level, so it is defined once and answered per
/// application; the answers live in the application's JSONB bag keyed by this
/// definition's id. <see cref="OwnerId"/> is a non-FK reference to an Identity
/// account - no cross-schema foreign key, ever.
/// <para>
/// Definitions are archived, never deleted. The values recorded against one
/// outlive it: deleting the definition would strand every answer that referenced
/// it, with nothing left to say what the answer meant. An archived field stops
/// being offered on new applications and keeps everything already recorded.
/// </para>
/// </summary>
internal sealed class CustomFieldDefinition
{
    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    /// <summary>What the user calls this field. Unique among their unarchived ones.</summary>
    public required string Label { get; set; }

    /// <summary>
    /// The label folded to lower case by the database, which is what the uniqueness
    /// index actually compares - "Referral" and "referral" are the same field name
    /// to a person reading a form. Generated, so nothing can write it out of step
    /// with the label it mirrors.
    /// </summary>
    public string LabelNormalized { get; private set; } = string.Empty;

    /// <summary>Fixed at creation; see <see cref="CustomFieldType"/>.</summary>
    public CustomFieldType Type { get; set; }

    /// <summary>
    /// The choices, for the select types only; empty for every other type. Editable
    /// - a user's list of sources or teams grows - and a value recorded against an
    /// option that is later removed is kept as it was rather than blanked.
    /// </summary>
    public string[] Options { get; set; } = [];

    /// <summary>
    /// Retired: no longer offered when filling in an application, still readable so
    /// the answers already given continue to mean something.
    /// </summary>
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the definition is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
