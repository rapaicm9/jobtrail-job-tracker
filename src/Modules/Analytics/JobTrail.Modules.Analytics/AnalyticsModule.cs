using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Analytics.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobTrail.Modules.Analytics;

/// <summary>
/// The Analytics module's composition surface. A host calls
/// <see cref="AddAnalyticsModule"/> to register the read model's store;
/// everything the module owns stays internal behind it. The projections that
/// fill it and the endpoints that read it arrive in later slices.
/// </summary>
public static class AnalyticsModule
{
    public static IHostApplicationBuilder AddAnalyticsModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobtrail")
            ?? throw new InvalidOperationException(
                "Connection string 'jobtrail' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<AnalyticsDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, AnalyticsDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<AnalyticsDbContext>();

        // The clock every handler here dates its writes by. Registered defensively:
        // the module should stand up whether or not another one got here first.
        builder.Services.TryAddSingleton(TimeProvider.System);

        return builder;
    }
}
