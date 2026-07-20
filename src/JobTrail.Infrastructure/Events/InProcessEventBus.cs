using JobTrail.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;

namespace JobTrail.Infrastructure.Events;

/// <summary>
/// Hands the event to the dispatcher's queue and returns. Handler resolution is
/// captured here, where <typeparamref name="TEvent"/> is known, but nothing is
/// resolved yet - that happens in the dispatcher's own scope, long after the
/// publishing request's scope has been disposed.
/// </summary>
internal sealed class InProcessEventBus(IntegrationEventChannel channel) : IEventBus
{
    public ValueTask PublishAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Note the dispatch type is the static TEvent, not the runtime type:
        // publishing a UserRegistered through an IIntegrationEvent-typed
        // variable would look for IEventHandler<IIntegrationEvent> and find
        // nothing. Publish concrete events - the compiler infers TEvent
        // correctly at every real call site.
        var envelope = new IntegrationEventEnvelope(
            typeof(TEvent).Name,
            provider => ResolveHandlers(provider, integrationEvent));

        return channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    private static IReadOnlyList<HandlerInvocation> ResolveHandlers<TEvent>(
        IServiceProvider provider,
        TEvent integrationEvent)
        where TEvent : IIntegrationEvent =>
        [
            .. provider
                .GetServices<IEventHandler<TEvent>>()
                .Select(handler => new HandlerInvocation(
                    handler.GetType().Name,
                    cancellationToken => handler.HandleAsync(integrationEvent, cancellationToken))),
        ];
}
