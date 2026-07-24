using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// Composition surface for durable event delivery: a module turns on a dispatcher
/// over its own store and names the events it records there.
/// </summary>
public static class OutboxRegistration
{
    /// <summary>
    /// Registers a dispatcher for the outbox rows in <typeparamref name="TDbContext"/>.
    /// Called by the module that owns the store, which is also the only place that
    /// knows which events it publishes durably.
    /// </summary>
    public static IHostApplicationBuilder AddOutboxDispatcher<TDbContext>(
        this IHostApplicationBuilder builder,
        Action<OutboxEventRegistry> configure)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var registry = new OutboxEventRegistry();
        configure(registry);

        var options = ReadOptions(builder.Configuration);

        // Registered as a factory rather than by type, so the registry and options
        // built here travel with this dispatcher - a second module registering its
        // own gets its own, over its own store.
        builder.Services.AddSingleton<IHostedService>(provider => new OutboxDispatcher<TDbContext>(
            registry,
            options,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<OutboxDispatcher<TDbContext>>>()));

        return builder;
    }

    /// <summary>
    /// The poll interval is the one knob worth turning per environment - a test
    /// host wants events delivered in milliseconds, not on the production cadence.
    /// Everything else stays at the defaults, which are properties of the workload
    /// rather than of where it runs.
    /// </summary>
    private static OutboxOptions ReadOptions(IConfiguration configuration) =>
        int.TryParse(
            configuration["Outbox:PollIntervalMs"], CultureInfo.InvariantCulture, out var milliseconds)
        && milliseconds > 0
            ? new OutboxOptions { PollInterval = TimeSpan.FromMilliseconds(milliseconds) }
            : new OutboxOptions();
}
