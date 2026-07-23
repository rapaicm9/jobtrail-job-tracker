using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// A single job application - the module's aggregate root, and the thing a user
/// spends the app recording. It owns the pipeline invariant: <see cref="Stage"/>
/// changes only through <see cref="TransitionTo"/>, which enforces the state
/// machine so an illegal position is unreachable rather than merely
/// rejected downstream. The built-in fields, the campaign and company
/// references, and the custom-field bag arrive in their own slices.
/// <see cref="OwnerId"/> is a non-FK reference to an Identity account - no
/// cross-schema foreign key, ever.
/// </summary>
internal sealed class Application
{
    public Guid Id { get; set; }

    public UserId OwnerId { get; set; }

    /// <summary>
    /// Where the application sits in the pipeline. Starts at <see cref="Stage.Applied"/>
    /// - v1 has no earlier stage - and moves only via <see cref="TransitionTo"/>.
    /// </summary>
    public Stage Stage { get; private set; } = Stage.Applied;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the application is next modified; null until then.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Moves the application to <paramref name="target"/> if the state machine
    /// allows it, stamping <see cref="UpdatedAt"/> and returning the recorded
    /// <see cref="StageTransition"/> (which the caller logs and publishes). An
    /// illegal move - a backwards or same-stage step between active stages, or
    /// <c>Accepted</c> from anywhere but <c>Offer</c> - leaves the application
    /// untouched and returns <see cref="ApplicationErrors.IllegalTransition"/>.
    /// Rules:
    /// <list type="bullet">
    /// <item>active → a strictly later active stage (skips allowed);</item>
    /// <item><c>Rejected</c>/<c>Withdrawn</c>/<c>Ghosted</c> from any active stage;</item>
    /// <item><c>Accepted</c> from <c>Offer</c> only - never out of a terminal stage;</item>
    /// <item>terminal → any active stage (reopening, logged);</item>
    /// <item>terminal → another terminal outcome except <c>Accepted</c> (reclassifying).</item>
    /// </list>
    /// </summary>
    public Result<StageTransition> TransitionTo(Stage target, DateTimeOffset now)
    {
        var from = Stage;
        if (Classify(from, target) is not { } kind)
        {
            return ApplicationErrors.IllegalTransition(from, target);
        }

        Stage = target;
        UpdatedAt = now;
        return new StageTransition(from, target, kind);
    }

    /// <summary>
    /// The state machine itself: the kind of move <paramref name="from"/> →
    /// <paramref name="target"/> is, or null if the move is illegal. A same-stage
    /// move is never a transition.
    /// </summary>
    private static TransitionKind? Classify(Stage from, Stage target)
    {
        if (from == target)
        {
            return null;
        }

        return (from.IsActive(), target.IsActive()) switch
        {
            // Active → active: forward only, to a strictly later stage.
            (true, true) => target.PipelineIndex() > from.PipelineIndex() ? TransitionKind.Advance : null,

            // Active → terminal: Accepted needs Offer; the rest reach from any active stage.
            (true, false) => target is not Stage.Accepted || from is Stage.Offer ? TransitionKind.Terminal : null,

            // Terminal → active: reopening a closed application.
            (false, true) => TransitionKind.Reopen,

            // Terminal → terminal: correcting the outcome (a ghost that finally
            // sends the rejection). Accepted is the exception - it stays reachable
            // from Offer alone, so it can't be reached out of another terminal.
            (false, false) => target is Stage.Accepted ? null : TransitionKind.Reclassify,
        };
    }
}
