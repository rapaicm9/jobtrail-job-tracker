using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Maps kernel failures onto RFC 9457 ProblemDetails at the API edge. The third
/// near-copy of this mapper (after Identity and Billing), which is the point the
/// duplication earns a shared web-edge home: extracting it now can retire all
/// three copies at once. Kept local for this slice so the endpoint lands as one
/// change; the extraction is its own.
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

    private static int StatusOf(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError,
    };
}
