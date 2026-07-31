using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Notifications.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobTrail.Modules.Notifications;

/// <summary>
/// The Notifications module's composition surface. A host calls
/// <see cref="AddNotificationsModule"/> to register the reminder store; everything
/// the module owns stays internal behind it. The handlers that arm reminders, the
/// sweep that delivers them and the feed that lists them arrive in later slices.
/// <para>
/// Two hosts will call this. Reminders are armed in the API host, where the outbox
/// dispatcher runs and the events arrive; they are delivered from the worker, where
/// the schedule lives. One table, one writer, one reader.
/// </para>
/// </summary>
public static class NotificationsModule
{
    public static IHostApplicationBuilder AddNotificationsModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobtrail")
            ?? throw new InvalidOperationException(
                "Connection string 'jobtrail' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<NotificationsDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, NotificationsDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<NotificationsDbContext>();

        return builder;
    }
}
