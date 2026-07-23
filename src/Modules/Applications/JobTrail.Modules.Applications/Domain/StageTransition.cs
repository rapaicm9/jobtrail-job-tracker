namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// How a legal stage change relates to the pipeline. The kind is what later
/// slices key off - a <see cref="Reopen"/> is logged as such, and the activity
/// timeline and <c>ApplicationStageChanged</c> event distinguish it from an
/// ordinary advance.
/// </summary>
internal enum TransitionKind
{
    /// <summary>Active → a strictly later active stage.</summary>
    Advance,

    /// <summary>Active → a terminal outcome.</summary>
    Terminal,

    /// <summary>Terminal → active: a closed application brought back to life.</summary>
    Reopen,

    /// <summary>
    /// Terminal → another terminal outcome: correcting how an application ended
    /// (a ghost that finally sends the rejection) without reopening it first.
    /// </summary>
    Reclassify,
}

/// <summary>
/// The record of one accepted stage change. Carries the <see cref="From"/> stage
/// - lost from the aggregate the instant the move is applied - so the caller can
/// write the activity-log entry and publish <c>ApplicationStageChanged</c>
/// without having captured it beforehand.
/// </summary>
internal readonly record struct StageTransition(Stage From, Stage To, TransitionKind Kind);
