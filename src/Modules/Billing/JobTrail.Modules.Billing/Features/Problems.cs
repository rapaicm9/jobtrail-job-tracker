using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobTrail.Modules.Billing.Features;

/// <summary>
/// Maps kernel failures onto RFC 9457 ProblemDetails at the API edge. A near-copy
/// of Identity's own mapper: Billing is the second module to need it, and the
/// natural next step is a shared web-edge home - deferred to when a third module
/// arrives, so extracting it can migrate every copy at once rather than half.
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
