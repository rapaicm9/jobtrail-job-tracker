using JobTrail.Infrastructure.Events;
using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Billing.Features.ProvisionPlan;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobTrail.Modules.Billing;

/// <summary>
/// The Billing module's composition surface. A host calls
/// <see cref="AddBillingModule"/> to register the entitlement store and its event
/// reactions; everything the module owns stays internal behind it. Endpoints and
/// the entitlement query arrive in later slices.
/// </summary>
public static class BillingModule
{
    public static IHostApplicationBuilder AddBillingModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobtrail")
            ?? throw new InvalidOperationException(
                "Connection string 'jobtrail' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<BillingDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, BillingDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<BillingDbContext>();

        // Every new account gets its Free plan, off Identity's UserRegistered.
        builder.Services.AddEventHandler<UserRegistered, PlanProvisioningHandler>();

        return builder;
    }
}
