using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features.TransitionApplication;

/// <summary>
/// Checks only that the request names a real stage - an unknown or missing stage
/// is a 422 keyed to <c>targetStage</c>. Whether moving <em>to</em> that stage is
/// allowed is a different failure the aggregate raises, also a 422 but with its
/// own code, so a client can tell "no such stage" from "not a legal move".
/// </summary>
internal static class TransitionApplicationRequestValidator
{
    public static Dictionary<string, string[]>? Validate(TransitionApplicationRequest request)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.TargetStage))
        {
            errors.Add("targetStage", "A target stage is required.");
        }
        else if (!(Enum.TryParse<Stage>(request.TargetStage, ignoreCase: true, out var stage) && Enum.IsDefined(stage)))
        {
            errors.Add("targetStage", "The target stage is not a known pipeline stage.");
        }

        return errors.ToResultOrNull();
    }
}
