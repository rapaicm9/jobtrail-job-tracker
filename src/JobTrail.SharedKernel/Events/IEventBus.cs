namespace JobTrail.SharedKernel.Events;

/// <summary>
/// Publishes integration events to whichever handlers are registered for them.
/// <para>
/// Publishing is fire-and-forget by design: the call returns once the event is
/// accepted for dispatch, not once handlers have run. A publisher therefore
/// learns nothing about its consumers - which is the point, and what keeps a
/// module from quietly depending on another module's reaction.
/// </para>
/// <para>
/// Delivery is at-least-once and handlers must be idempotent. The in-process
/// implementation loses queued events if the host dies mid-dispatch; that is
/// acceptable for reactions which can be rebuilt, and events whose loss has
/// real consequences (anything that schedules a reminder) will be published
/// through the transactional outbox behind this same interface.
/// </para>
/// </summary>
public interface IEventBus
{
    /// <param name="integrationEvent">The fact to publish.</param>
    /// <param name="cancellationToken">
    /// Cancels acceptance of the event, not its handlers. Once accepted, an
    /// event is dispatched on the host's lifetime rather than the caller's, so
    /// a finished HTTP request never cuts a handler short.
    /// </param>
    ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
