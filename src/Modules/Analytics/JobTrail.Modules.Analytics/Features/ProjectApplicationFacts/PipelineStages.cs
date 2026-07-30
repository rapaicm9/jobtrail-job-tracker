namespace JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;

/// <summary>
/// The stage names this module recognises, and what it reads into them.
/// <para>
/// A names-only echo of a pipeline the Applications module owns - its <c>Stage</c>
/// enum is internal to it, and mirroring the type would mean this module claiming
/// knowledge it has no way to keep current. Names are the least that can be known
/// and still build a funnel, and nothing here throws on a name it does not
/// recognise: an unknown stage is stored as itself and simply sets no funnel
/// column.
/// </para>
/// </summary>
internal static class PipelineStages
{
    /// <summary>
    /// Where an application starts. The one piece of pipeline knowledge this
    /// module asserts rather than reads off an event, and it is asserted because
    /// <c>ApplicationSubmitted</c> carries no stage: without it a newly recorded
    /// application would have none, and drop out of the pipeline snapshot - the
    /// Free tier's headline figure - until its first transition.
    /// <para>
    /// If the pipeline ever gains a stage before this one, that event has to start
    /// carrying the stage it opened in, and this constant goes away. It is not
    /// something that can be inferred here.
    /// </para>
    /// </summary>
    public const string Applied = "Applied";

    public const string Screening = "Screening";

    public const string Interview = "Interview";

    public const string Offer = "Offer";

    private const string Accepted = "Accepted";

    private const string Rejected = "Rejected";

    /// <summary>
    /// Whether arriving at this stage means the employer came back.
    /// <para>
    /// Deliberately not "any move off Applied". Being ghosted is the <em>absence</em>
    /// of a response, and withdrawing is the user's own act - counting either would
    /// put the applications that most clearly never got an answer into the
    /// numerator of the response rate.
    /// </para>
    /// </summary>
    public static bool IsResponse(string stage) =>
        stage is Screening or Interview or Offer or Accepted or Rejected;
}
