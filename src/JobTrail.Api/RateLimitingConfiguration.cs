using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JobTrail.Api;

/// <summary>
/// Per-IP rate limiting with the built-in middleware - no third-party
/// dependency, in-memory until there is more than one instance (§ ADR-0002
/// posture: adopt infrastructure when the trigger fires, not before).
/// <para>
/// Two named policies: a sliding window over the whole API surface, and a
/// stricter fixed window for the auth endpoints. Policies are endpoint
/// metadata, so health endpoints - which the orchestrator polls constantly -
/// are never throttled. On the auth group the innermost policy wins, which is
/// exactly the intent: those endpoints trade the general budget for the
/// stricter one.
/// </para>
/// </summary>
internal static class RateLimitingConfiguration
{
    /// <summary>Sliding window per IP, applied to the versioned API group.</summary>
    public const string GlobalPolicy = "per-ip";

    /// <summary>Stricter fixed window per IP for <c>/identity</c> - login, register, refresh.</summary>
    public const string AuthPolicy = "auth";

    public static IHostApplicationBuilder AddApiRateLimiting(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<RateLimitingOptions>(
            builder.Configuration.GetSection(RateLimitingOptions.SectionName));

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteProblemAsync;

            limiter.AddPolicy(GlobalPolicy, context =>
            {
                var options = Limits(context);
                return RateLimitPartition.GetSlidingWindowLimiter(ClientKey(context), _ =>
                    new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalPermitLimit,
                        Window = options.GlobalWindow,
                        SegmentsPerWindow = options.GlobalSegmentsPerWindow,
                        QueueLimit = 0,
                    });
            });

            limiter.AddPolicy(AuthPolicy, context =>
            {
                var options = Limits(context);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitLimit,
                        Window = options.AuthWindow,
                        QueueLimit = 0,
                    });
            });
        });

        return builder;
    }

    /// <summary>
    /// One partition per client address. Once Caddy terminates TLS in front,
    /// the forwarded-headers middleware (hardening slice) restores the real
    /// client address before this runs; in dev the connection address is the
    /// client. A null address (in-process test server) shares one partition.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitingOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    /// <summary>429 as ProblemDetails, with Retry-After so well-behaved clients back off.</summary>
    private static async ValueTask WriteProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = http,
                ProblemDetails =
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "The request rate limit was exceeded. Slow down and retry.",
                    Type = "https://tools.ietf.org/html/rfc6585#section-4",
                },
            });
    }
}
