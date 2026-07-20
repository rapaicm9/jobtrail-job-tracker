using JobTrail.SharedKernel.Events;

namespace JobTrail.Infrastructure.Events;

/// <summary>
/// A published event on its way to the dispatcher.
/// <para>
/// The event itself is not carried as <see cref="IIntegrationEvent"/>, because
/// the dispatcher would then have to rediscover its concrete type and build a
/// closed <see cref="IEventHandler{TEvent}"/> by reflection. Instead the bus
/// captures the resolution while the type argument is still statically known,
/// and hands the dispatcher a delegate. No reflection, and a handler signature
/// that cannot drift from what the compiler checked at the publish site.
/// </para>
/// </summary>
/// <param name="EventName">The event type's name, for logging only.</param>
/// <param name="ResolveHandlers">
/// Resolves the handlers registered for this event from a scope the dispatcher
/// owns, and binds each to the event instance.
/// </param>
internal sealed record IntegrationEventEnvelope(
    string EventName,
    Func<IServiceProvider, IReadOnlyList<HandlerInvocation>> ResolveHandlers);

/// <summary>One resolved handler, ready to run.</summary>
/// <param name="HandlerName">The handler type's name, for logging only.</param>
/// <param name="InvokeAsync">Runs the handler against the captured event.</param>
internal sealed record HandlerInvocation(string HandlerName, Func<CancellationToken, Task> InvokeAsync);
