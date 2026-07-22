using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobTrail.Modules.Applications;

/// <summary>
/// The Applications module's composition surface. A host calls
/// <see cref="AddApplicationsModule"/> to register the application store;
/// everything the module owns stays internal behind it. The aggregate's
/// behaviour, endpoints and event reactions arrive in later slices.
/// </summary>
public static class ApplicationsModule
{
    public static IHostApplicationBuilder AddApplicationsModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobtrail")
            ?? throw new InvalidOperationException(
                "Connection string 'jobtrail' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<ApplicationsDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, ApplicationsDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<ApplicationsDbContext>();

        return builder;
    }
}
