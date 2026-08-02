using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
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
/// The worker composes the reminder store and the schedule, and neither the
/// handlers that arm reminders nor the Identity module those handlers read the
/// owner's timezone through. Container validation is on by default in Development,
/// so getting this wrong does not surface as a dormant registration - it stops the
/// worker starting, on a dependency it has no reason to hold.
/// </para>
/// <para>
/// That failure has no unit test's shape and would otherwise only ever be caught by
/// someone running the AppHost, which is exactly the sort of check that stops being
/// run. So it is asserted here instead: the shared composition method is built in
/// isolation, under the same validation the worker gets.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsCompositionTests(ApiFixture fixture)
{
    [Fact]
    public void The_shared_composition_stands_up_without_identity()
    {
        var services = ComposeAsTheWorkerDoes();

        // Exactly what a Development host does on the way up, and what would throw
        // if an arming handler had been registered here.
        Should.NotThrow(() => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose());
    }

    [Fact]
    public void The_shared_composition_registers_no_event_handlers()
    {
        var services = ComposeAsTheWorkerDoes();

        // The store, yes; reacting to another module's events, no - the worker
        // composes no dispatcher, so a handler registered here could never run.
        services.ShouldNotContain(service =>
            service.ServiceType == typeof(IEventHandler<InterviewScheduled>));
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
    /// store and is not what these tests are about. The connection string is the
    /// fixture's, so the context registers against something real; nothing here
    /// opens a connection.
    /// </summary>
    private IServiceCollection ComposeAsTheWorkerDoes()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(fixture.BuildSettings());
        builder.AddNotificationsModule();

        return builder.Services;
    }
}
