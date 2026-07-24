using JobTrail.Infrastructure.Events;
using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Features.CreateApplication;
using JobTrail.Modules.Applications.Features.GetApplication;
using JobTrail.Modules.Applications.Features.ListApplications;
using JobTrail.Modules.Applications.Features.ProvisionCampaign;
using JobTrail.Modules.Applications.Features.SearchCompanies;
using JobTrail.Modules.Applications.Features.TransitionApplication;
using JobTrail.Modules.Applications.Features.UpdateApplication;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
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

        // Every new account gets its default campaign, off Identity's UserRegistered.
        builder.Services.AddEventHandler<UserRegistered, CampaignProvisioningHandler>();

        builder.Services.AddScoped<SearchCompaniesHandler>();
        builder.Services.AddScoped<CompanyResolver>();
        builder.Services.AddScoped<CreateApplicationHandler>();
        builder.Services.AddScoped<GetApplicationHandler>();
        builder.Services.AddScoped<ListApplicationsHandler>();
        builder.Services.AddScoped<UpdateApplicationHandler>();
        builder.Services.AddScoped<TransitionApplicationHandler>();

        return builder;
    }

    /// <summary>
    /// Maps the Applications module's authenticated slices onto the host's
    /// versioned API group. The module owns several top-level resources, so this
    /// mounts each on its own group (<c>/companies</c> now; the application,
    /// campaign and interview groups join as their slices land) - it is the
    /// module's endpoints, not one route. Takes the host's general per-IP budget.
    /// </summary>
    public static void MapApplicationsEndpoints(this IEndpointRouteBuilder api)
    {
        var companies = api.MapGroup("/companies");
        SearchCompaniesEndpoint.Map(companies);

        var applications = api.MapGroup("/applications");
        ListApplicationsEndpoint.Map(applications);
        CreateApplicationEndpoint.Map(applications);
        GetApplicationEndpoint.Map(applications);
        UpdateApplicationEndpoint.Map(applications);
        TransitionApplicationEndpoint.Map(applications);
    }
}
