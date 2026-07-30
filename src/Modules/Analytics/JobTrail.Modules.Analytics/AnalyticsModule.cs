using JobTrail.Infrastructure.Events;
using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Analytics.Features.EraseData;
using JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;
using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.Modules.Identity.Contracts;
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

        // Everything the read model is built from. Each of these is an upsert on
        // the application id, so redelivery is harmless and the order they arrive
        // in does not decide what the row ends up saying.
        //
        // InterviewCancelled is not among them on purpose: the column it would
        // touch records that a round was once booked, which a cancellation does
        // not undo.
        builder.Services.AddScoped<ApplicationFactsWriter>();
        builder.Services.AddEventHandler<ApplicationSubmitted, ApplicationSubmittedProjection>();
        builder.Services.AddEventHandler<ApplicationStageChanged, ApplicationStageChangedProjection>();
        builder.Services.AddEventHandler<ApplicationReachedTerminal, ApplicationReachedTerminalProjection>();
        builder.Services.AddEventHandler<ApplicationReopened, ApplicationReopenedProjection>();
        builder.Services.AddEventHandler<ApplicationMovedToCampaign, ApplicationMovedToCampaignProjection>();
        builder.Services.AddEventHandler<InterviewScheduled, InterviewScheduledProjection>();

        // And gives it all back on the way out: this module's share of the erasure
        // fan-out, from its own schema only.
        builder.Services.AddEventHandler<UserDataDeletionRequested, AnalyticsDataErasureHandler>();

        // The clock every handler here dates its writes by. Registered defensively:
        // the module should stand up whether or not another one got here first.
        builder.Services.TryAddSingleton(TimeProvider.System);

        return builder;
    }
}
