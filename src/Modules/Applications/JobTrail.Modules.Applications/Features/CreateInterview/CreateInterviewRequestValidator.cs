namespace JobTrail.Modules.Applications.Features.CreateInterview;

/// <summary>
/// Shape-level checks on a create-interview request, keyed by field - the shared
/// interview rules, with no outcome to check since a new round is pending. That
/// the parent application is the caller's own is the handler's job.
/// </summary>
internal static class CreateInterviewRequestValidator
{
    public static Dictionary<string, string[]>? Validate(CreateInterviewRequest request)
    {
        var errors = new ValidationErrors();
        InterviewFieldValidation.Validate(request, errors);
        return errors.ToResultOrNull();
    }
}
