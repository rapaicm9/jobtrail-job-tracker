using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Identity;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Notifications;
using Jobspect.SharedKernel.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The seam between what both hosts compose and what only the API does.
/// <para>
/// The worker composes the reminder store, the schedule, and one narrow read across
/// a module boundary - Identity's profile query, because the follow-up scan raises
/// reminders at 11:00 in the owner's own timezone. What it does not compose is the
/// handlers that arm reminders: they need a dispatcher and an event bus this host
/// has neither of, so a handler registered here could never run.
/// </para>
/// <para>
/// <b>The narrow read is why <c>AddIdentityProfileQuery</c> exists rather than the
/// whole module.</b> Composing Identity outright would bring its outbox dispatcher,
/// which claims owed events and marks them processed once its handlers have run -
/// and this host registers none, so it would consume every registration event and
/// deliver it nowhere. That is the failure this file is really guarding.
/// </para>
/// <para>
/// Container validation is on by default in Development, so getting the composition
/// wrong does not surface as a dormant registration - it stops the worker starting.
/// That failure has no unit test's shape and would otherwise only ever be caught by
/// someone running the AppHost, which is exactly the sort of check that stops being
/// run. So it is asserted here instead, under the same validation the worker gets.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsCompositionTests(ApiFixture fixture)
{
    [Fact]
    public void The_workers_composition_stands_up()
    {
        var services = ComposeAsTheWorkerDoes();

        // Exactly what a Development host does on the way up, and what would throw
        // if an arming handler had been registered here.
        Should.NotThrow(() => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose());
    }

    [Fact]
    public void The_workers_composition_registers_no_event_handlers()
    {
        var services = ComposeAsTheWorkerDoes();

        // The store and the profile read, yes; reacting to another module's events,
        // no - this host composes no dispatcher, so a handler registered here could
        // never run.
        services.ShouldNotContain(service =>
            service.ServiceType == typeof(IEventHandler<InterviewScheduled>));
    }

    /// <summary>
    /// The half of the Identity module the worker takes, and the half it must not.
    /// An outbox dispatcher is a hosted service, so it would start with the host and
    /// begin claiming events nothing here handles - the events would be marked
    /// delivered having reached no one.
    /// </summary>
    [Fact]
    public void The_workers_composition_takes_the_profile_read_without_the_dispatcher()
    {
        var services = ComposeAsTheWorkerDoes();

        services.ShouldContain(service => service.ServiceType == typeof(IUserProfileQuery));

        services.Where(service => service.ServiceType == typeof(IHostedService))
            .Select(service => service.ImplementationType?.Name ?? string.Empty)
            .ShouldNotContain(name => name.Contains("Outbox", StringComparison.Ordinal));
    }

    [Fact]
    public void The_api_host_does_arm_reminders()
    {
        // The other half of the seam: the handlers exist where the events arrive.
        // Without this, the test above would pass just as well on a module that had
        // quietly stopped consuming anything at all.
        using var scope = fixture.CreateScope();

        scope.ServiceProvider
            .GetServices<IEventHandler<InterviewScheduled>>()
            .ShouldContain(handler => handler.GetType().Assembly == typeof(NotificationsModule).Assembly);
    }

    /// <summary>
    /// The worker's composition root, minus the schedule - which needs a live job
    /// store and is proven in <see cref="NotificationsSchedulerTests"/> instead. The
    /// connection string is the fixture's, so the contexts register against something
    /// real; nothing here opens a connection.
    /// </summary>
    private IServiceCollection ComposeAsTheWorkerDoes()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(fixture.BuildSettings());
        builder.AddNotificationsModule();
        builder.AddIdentityProfileQuery();

        return builder.Services;
    }
}
