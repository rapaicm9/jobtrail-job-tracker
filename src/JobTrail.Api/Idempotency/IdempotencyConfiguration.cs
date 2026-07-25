using System.Globalization;

namespace JobTrail.Api.Idempotency;

/// <summary>
/// Composition surface for the replay cache. It resolves the Redis client the
/// host registers for the "cache" resource - the same multiplexer the Data
/// Protection key ring uses, one connection for both.
/// </summary>
internal static class IdempotencyConfiguration
{
    public static IHostApplicationBuilder AddApiIdempotency(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(ReadOptions(builder.Configuration));
        builder.Services.AddSingleton<IdempotencyStore>();

        return builder;
    }

    public static IApplicationBuilder UseIdempotencyKeys(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();

    /// <summary>
    /// The in-flight window is the one knob worth turning per environment: a test
    /// host wants a lapsed reservation in seconds, not a minute. The rest are
    /// properties of the workload rather than of where it runs.
    /// </summary>
    private static IdempotencyOptions ReadOptions(IConfiguration configuration) =>
        int.TryParse(
            configuration["Idempotency:InFlightSeconds"], CultureInfo.InvariantCulture, out var seconds)
        && seconds > 0
            ? new IdempotencyOptions { InFlightWindow = TimeSpan.FromSeconds(seconds) }
            : new IdempotencyOptions();
}
