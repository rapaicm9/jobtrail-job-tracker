namespace JobTrail.Modules.Applications.Features.TransitionApplication;

/// <summary>
/// The one input a pipeline move needs: the stage to move to. Whether the move is
/// legal is the aggregate's call, not the request's - this only names a
/// destination, given as the stage's name (e.g. <c>"Offer"</c>).
/// </summary>
internal sealed record TransitionApplicationRequest(string? TargetStage);
