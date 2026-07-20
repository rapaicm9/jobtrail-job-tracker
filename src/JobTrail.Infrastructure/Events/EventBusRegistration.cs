using JobTrail.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobTrail.Infrastructure.Events;

/// <summary>
/// Composition surface for the event bus: a host turns dispatch on, and each
/// module registers its own handlers.
/// </summary>
public static class EventBusRegistration
{
    /// <summary>
    /// Registers in-process event dispatch. Called by a host, once, before the
    /// modules that publish or handle events.
    /// </summary>
    public static IServiceCollection AddInProcessEventBus(this IServiceCollection services)
    {
        // The queue and the bus are stateless singletons; the dispatcher makes
        // its own scope per event, so nothing scoped is captured here.
        services.TryAddSingleton<IntegrationEventChannel>();
        services.TryAddSingleton<IEventBus, InProcessEventBus>();

        // AddHostedService is idempotent for a given implementation type, so a
        // host calling this twice still gets exactly one dispatcher.
        services.AddHostedService<IntegrationEventDispatcher>();

        return services;
    }

    /// <summary>
    /// Registers one handler for one event. Scoped, so a handler may depend on
    /// its module's DbContext exactly as an endpoint handler does.
    /// <para>
    /// Several handlers may be registered for the same event, including from
    /// different modules; all of them run, and none can see the others.
    /// </para>
    /// </summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddScoped<IEventHandler<TEvent>, THandler>();

        return services;
    }
}
