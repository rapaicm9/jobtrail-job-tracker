namespace JobTrail.SharedKernel;

/// <summary>
/// The shape of a failure, so a caller can react without parsing a message.
/// Maps to an HTTP status at the API edge (§4 ProblemDetails), never here -
/// the kernel stays transport-agnostic.
/// </summary>
public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
}

/// <summary>
/// A failure value carried by <see cref="Result"/>. <see cref="Code"/> is a
/// stable, machine-readable slug ("refresh_token.reuse_detected"); <see cref="Message"/>
/// is a human-readable sentence. Errors are compared by value.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    /// <summary>The absence of an error, held by a successful result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
}
