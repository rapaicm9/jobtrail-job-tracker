using System.Globalization;
using System.Text;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using StackExchange.Redis;

namespace Jobspect.Api.Idempotency;

/// <summary>
/// Makes a retried mutation happen once. A client that sends an
/// <c>Idempotency-Key</c> and never learns the outcome - a dropped connection, a
/// phone that changed networks mid-request - can send the same request again and
/// receive the original response instead of causing a second one.
/// <para>
/// It runs on every authenticated <c>POST</c> that carries the header. POST is
/// the only method that needs it: a full-replace PUT applied twice leaves the
/// same state, and so does a DELETE. The unauthenticated auth surface is outside
/// this by construction - its responses carry tokens, which have no business
/// sitting in a cache for a day, and it has no user to scope a key by. The
/// retried-refresh problem that leaves is a token-model matter, not a replay one.
/// </para>
/// <para>
/// Middleware rather than an endpoint filter because the fingerprint needs the
/// raw body, which model binding has already consumed by the time a filter runs.
/// </para>
/// </summary>
internal sealed partial class IdempotencyMiddleware(
    RequestDelegate next,
    IdempotencyStore store,
    IdempotencyOptions options,
    IProblemDetailsService problemDetails,
    ILogger<IdempotencyMiddleware> logger)
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Set on a replayed response, so a client can tell one from a fresh execution.</summary>
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Applies(context, out var key) || CallerOf(context) is not { } owner)
        {
            await next(context);
            return;
        }

        if (!IsWellFormed(key))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "idempotency.key_invalid",
                $"The {HeaderName} header must be 1 to {options.MaxKeyLength} printable characters.");
            return;
        }

        try
        {
            await HandleAsync(context, owner, key);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Fail rather than run the request un-deduplicated. A caller that asked
            // for exactly-once and quietly got best-effort has no way to find out,
            // and a duplicate application is worse than a retry.
            CacheUnreachable(exception);
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "idempotency.unavailable",
                "The request could not be processed idempotently. Retry with the same key.",
                retryAfter: TimeSpan.FromSeconds(5));
        }
    }

    private async Task HandleAsync(HttpContext context, UserId owner, string key)
    {
        var fingerprint = await IdempotencyFingerprint.OfAsync(context.Request, options.MaxBodyBytes);
        var reservation = IdempotencyRecord.Reserve(fingerprint);

        if (!await store.TryReserveAsync(owner, key, reservation))
        {
            await ReplayAsync(context, owner, key, fingerprint);
            return;
        }

        await ExecuteAndRecordAsync(context, owner, key, reservation);
    }

    /// <summary>
    /// Answers a request whose key is already spoken for: the stored response if
    /// there is one, a refusal if the key was claimed for a different request, and
    /// a conflict while the first one is still running.
    /// </summary>
    private async Task ReplayAsync(HttpContext context, UserId owner, string key, string fingerprint)
    {
        var existing = await store.ReadAsync(owner, key);

        if (existing is null)
        {
            // It expired between the failed reservation and this read. Rare, and
            // the honest answer is the same as any other in-flight collision:
            // nothing was replayed, so try again.
            await WriteInFlightAsync(context);
            return;
        }

        if (existing.Fingerprint != fingerprint)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "idempotency.key_reused",
                $"This {HeaderName} was already used for a different request.");
            return;
        }

        if (!existing.IsCompleted)
        {
            await WriteInFlightAsync(context);
            return;
        }

        context.Response.StatusCode = existing.Status!.Value;
        context.Response.Headers[ReplayedHeaderName] = "true";

        if (existing.ContentType is not null)
        {
            context.Response.ContentType = existing.ContentType;
        }

        if (existing.Location is not null)
        {
            context.Response.Headers.Location = existing.Location;
        }

        if (existing.Body is not null)
        {
            await context.Response.WriteAsync(existing.Body);
        }

        Replayed();
    }

    /// <summary>
    /// Runs the request with its response buffered, then decides what the key owes
    /// the next caller. Success is recorded for replay. A client error releases the
    /// key, since nothing changed and the caller must be free to correct the body
    /// and retry under the same one. A server error does neither: after a 5xx
    /// nobody can say whether the write committed, so the reservation is left to
    /// lapse - a retry inside the window is refused, and after it runs again.
    /// </summary>
    private async Task ExecuteAndRecordAsync(
        HttpContext context, UserId owner, string key, IdempotencyRecord reservation)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();

        try
        {
            context.Response.Body = buffer;
            await next(context);
        }
        finally
        {
            // Before anything else can throw: an exception handler upstream writes
            // its ProblemDetails to whatever stream it finds here.
            context.Response.Body = originalBody;
        }

        var status = context.Response.StatusCode;

        if (status is >= 200 and < 300 && buffer.Length <= options.MaxBodyBytes)
        {
            await store.CompleteAsync(owner, key, reservation, reservation.Complete(
                status,
                context.Response.ContentType,
                context.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null,
                Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length)));
        }
        else if (status is >= 400 and < 500 || buffer.Length > options.MaxBodyBytes)
        {
            await store.ReleaseAsync(owner, key, reservation);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
    }

    /// <summary>
    /// Whether this request is one a key applies to. An endpoint that does not
    /// require authorization is out of scope - which is what keeps the token-
    /// bearing auth surface out of the cache without a list to maintain.
    /// </summary>
    private static bool Applies(HttpContext context, out string key)
    {
        key = string.Empty;

        if (!HttpMethods.IsPost(context.Request.Method)
            || context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is null
            || context.User.Identity?.IsAuthenticated is not true
            || !context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            return false;
        }

        key = header.ToString();
        return true;
    }

    private static UserId? CallerOf(HttpContext context) =>
        context.RequestServices.GetRequiredService<IUserContext>().UserId;

    private bool IsWellFormed(string key) =>
        key.Length > 0
        && key.Length <= options.MaxKeyLength
        && key.All(character => character is >= ' ' and <= '~');

    private Task WriteInFlightAsync(HttpContext context) =>
        WriteProblemAsync(
            context,
            StatusCodes.Status409Conflict,
            "idempotency.in_flight",
            "An earlier request with this key is still being processed.",
            retryAfter: options.InFlightWindow);

    private async Task WriteProblemAsync(
        HttpContext context, int status, string code, string detail, TimeSpan? retryAfter = null)
    {
        context.Response.StatusCode = status;

        if (retryAfter is { } after)
        {
            context.Response.Headers.RetryAfter =
                ((int)after.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = status,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The idempotency cache could not be reached; the request was refused rather than "
            + "processed without its replay guarantee.")]
    private partial void CacheUnreachable(Exception exception);

    // The key itself stays out of the log: it is an opaque value a client chose,
    // and knowing that a replay happened is the part worth recording.
    [LoggerMessage(Level = LogLevel.Information, Message = "Replayed a stored response for an idempotency key.")]
    private partial void Replayed();
}
