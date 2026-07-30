using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobTrail.Modules.Analytics.Features;

/// <summary>
/// Maps kernel failures onto RFC 9457 ProblemDetails at the API edge. Each module
/// keeps its own copy by design: the mapping is small and reads better beside the
/// endpoints that use it, and a module can let its own status mapping diverge
/// without disturbing the others.
/// <para>
/// Narrower than its siblings, and it should stay that way while it can. This
/// module's endpoints take no request body, so there is nothing to validate into a
/// field-keyed 422 - the helper for that arrives if and when a slice needs it,
/// rather than sitting here unused.
/// </para>
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
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,

        // A dependency this module reads through, not this module. 503 rather than
        // 500 says the request was fine and is worth repeating - which is exactly
        // what one degraded panel on an otherwise working dashboard means.
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}
