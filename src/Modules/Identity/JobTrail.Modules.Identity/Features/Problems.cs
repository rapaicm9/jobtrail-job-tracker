using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobTrail.Modules.Identity.Features;

/// <summary>
/// Maps kernel failures onto RFC 9457 ProblemDetails at the API edge - the
/// kernel itself stays transport-agnostic. Lives in the module for now, per the
/// add-at-first-use rule; it moves to a shared home when a second module needs it.
/// </summary>
internal static class Problems
{
    /// <summary>
    /// A single-error problem. The stable error code travels in a <c>code</c>
    /// extension member so clients branch on it, never on the prose.
    /// </summary>
    public static ProblemHttpResult ToProblem(this Error error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: StatusOf(error.Type),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    /// <summary>
    /// Field-keyed request-validation failures as a 422 - not the framework's
    /// default 400, per the API conventions.
    /// </summary>
    public static ProblemHttpResult Validation(IDictionary<string, string[]> errors) =>
        TypedResults.Problem(new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
        });

    private static int StatusOf(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError,
    };
}
