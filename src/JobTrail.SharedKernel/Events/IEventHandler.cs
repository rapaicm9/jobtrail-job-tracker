namespace JobTrail.SharedKernel.Events;

/// <summary>
/// Reacts to one integration event. A module registers its handlers in its own
/// composition method; the publisher never names them.
/// <para>
/// Handlers run outside the publisher's transaction and outside its request, so
/// they must be idempotent: the same event may be delivered more than once, and
/// a handler that throws is retried by nothing - it is logged and dropped.
/// Several handlers may exist for one event; each is isolated from the others'
/// failures.
/// </para>
/// </summary>
public interface IEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
