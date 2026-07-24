using JobTrail.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;

namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// Which stored event names this module can deliver, and how. A module registers
/// each event once at composition; the dispatcher, which only ever holds a name
/// and a string of JSON, asks this to turn them back into a typed event and run
/// its handlers.
/// <para>
/// Registration captures the event type while it is still statically known, so
/// there is no reflection here and no closed generic built at runtime - the same
/// approach the in-process bus takes, and for the same reason: a handler
/// signature that cannot drift from what the compiler checked.
/// </para>
/// <para>
/// Unlike in-process dispatch, which isolates handlers from each other and
/// swallows their failures, delivery here <b>lets an exception out</b>. That is
/// what tells the dispatcher not to mark the row processed, which is what makes
/// the retry real.
/// </para>
/// </summary>
public sealed class OutboxEventRegistry
{
    private readonly Dictionary<string, Func<string, IServiceProvider, CancellationToken, Task>> _deliveries = [];

    /// <summary>
    /// Registers <typeparamref name="TEvent"/> under the name its rows carry.
    /// The name is given rather than derived, so it survives a rename of the record.
    /// </summary>
    public OutboxEventRegistry Register<TEvent>(string eventType)
        where TEvent : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        _deliveries.Add(eventType, DeliverAsync<TEvent>);
        return this;
    }

    public bool IsRegistered(string eventType) => _deliveries.ContainsKey(eventType);

    /// <summary>
    /// Deserializes a stored payload and runs every handler registered for its
    /// event, in the scope the caller supplies. Throws if the event name was never
    /// registered - a stored row nobody can read is a deployment mistake, and
    /// silently discarding it would lose the event the outbox exists to keep.
    /// </summary>
    public Task DeliverAsync(
        string eventType, string payload, IServiceProvider provider, CancellationToken cancellationToken) =>
        _deliveries.TryGetValue(eventType, out var deliver)
            ? deliver(payload, provider, cancellationToken)
            : throw new InvalidOperationException(
                $"No outbox event is registered under the name '{eventType}'.");

    private static async Task DeliverAsync<TEvent>(
        string payload, IServiceProvider provider, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var integrationEvent = OutboxSerialization.Deserialize<TEvent>(payload)
            ?? throw new InvalidOperationException(
                $"The stored payload for {typeof(TEvent).Name} deserialized to null.");

        // Sequential, and no exception is caught: the first handler to fail stops
        // the row being marked processed, and the whole event is retried. Handlers
        // are required to be idempotent precisely because of this.
        foreach (var handler in provider.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync(integrationEvent, cancellationToken);
        }
    }
}
