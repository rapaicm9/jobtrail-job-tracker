namespace JobTrail.SharedKernel;

/// <summary>
/// The outcome of an operation that either succeeds or fails with an
/// <see cref="Error"/>. Preferred over exceptions for expected, recoverable
/// failures (a reused refresh token, a missing resource) so handlers branch on
/// a value instead of catching. Exceptions remain for the genuinely exceptional.
/// </summary>
public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    /// <summary>Lets a handler <c>return someError;</c> where a Result is expected.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// A <see cref="Result"/> that carries a value on success. Construct it by
/// returning either a <typeparamref name="T"/> or an <see cref="Error"/> - the
/// implicit conversions pick the right case - so call sites stay terse.
/// </summary>
public readonly struct Result<T>
{
    private Result(T value)
    {
        RawValue = value;
        IsSuccess = true;
        Error = Error.None;
    }

    private Result(Error error)
    {
        RawValue = default;
        IsSuccess = false;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    private T? RawValue { get; }

    /// <summary>The success value; throws if the result is a failure.</summary>
    public T Value => IsSuccess
        ? RawValue!
        : throw new InvalidOperationException("A failed result carries no value; check IsSuccess first.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);
}
