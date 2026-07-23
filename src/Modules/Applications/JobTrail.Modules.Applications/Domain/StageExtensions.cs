namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// The active/terminal classification and pipeline ordering the state machine
/// runs on. Kept as one small table here rather than inferred from the enum's
/// declaration order, so the rules read explicitly and reordering the enum can't
/// silently change which moves are legal.
/// </summary>
internal static class StageExtensions
{
    /// <summary>The live pipeline in order; a forward move must land on a later entry.</summary>
    private static readonly Stage[] Pipeline = [Stage.Applied, Stage.Screening, Stage.Interview, Stage.Offer];

    public static bool IsActive(this Stage stage) => Array.IndexOf(Pipeline, stage) >= 0;

    public static bool IsTerminal(this Stage stage) => !stage.IsActive();

    /// <summary>Position in the active pipeline (0-based); -1 for a terminal stage.</summary>
    public static int PipelineIndex(this Stage stage) => Array.IndexOf(Pipeline, stage);
}
