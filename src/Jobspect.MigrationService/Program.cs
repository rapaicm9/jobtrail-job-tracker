// The deploy-time migration step, as its own process. It applies every module's
// outstanding migrations and exits; the API and the worker wait for it to succeed
// before they start.
//
// Nothing migrates on startup, and that is the reason this exists: during a
// rolling deploy two instances of a host would race to alter the same schema, and
// the loser's failure would be a crash loop rather than a clear error. One process
// that runs once, exits, and reports success or failure through its exit code is
// the whole design.

using Jobspect.Infrastructure.Persistence;
using Jobspect.MigrationService;
using Jobspect.Modules.Analytics;
using Jobspect.Modules.Applications;
using Jobspect.Modules.Billing;
using Jobspect.Modules.Identity;
using Jobspect.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Every module, because every module owns a schema. Each registers a migrator for
// its own store; this process reaches those and nothing else, and never names a
// DbContext - they stay internal to their modules.
builder.AddIdentityModule();
builder.AddBillingModule();
builder.AddApplicationsModule();
builder.AddAnalyticsModule();
builder.AddNotificationsModule();

builder.Services.AddSingleton<MigrationRunner>();

// Built, deliberately never run. Composing the modules also registers the outbox
// dispatchers two of them own, and hosted services start on Run - which this
// process must never do. Delivering events is the API host's job; this one opens
// the connections it needs, applies what is outstanding, and leaves.
using var host = builder.Build();

return await host.Services
    .GetRequiredService<MigrationRunner>()
    .RunAsync(CancellationToken.None);
