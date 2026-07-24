using JobTrail.Modules.Applications.Domain;

namespace JobTrail.Modules.Applications.Features.UpdateInterview;

/// <summary>
/// Shape-level checks on an update-interview request, keyed by field: the shared
/// interview rules plus a required, known outcome - update is where a round's
/// result is set, so the client must name one.
/// </summary>
internal static class UpdateInterviewRequestValidator
{
    public static Dictionary<string, string[]>? Validate(UpdateInterviewRequest request)
    {
        var errors = new ValidationErrors();
        InterviewFieldValidation.Validate(request, errors);

        if (!InterviewFieldValidation.ParsesTo<InterviewOutcome>(request.Outcome))
        {
            errors.Add("outcome", "The outcome must be one of Pending, Passed, Failed or Cancelled.");
        }

        return errors.ToResultOrNull();
    }
}
