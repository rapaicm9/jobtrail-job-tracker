using JobTrail.SharedKernel.Events;

namespace JobTrail.Modules.Identity.Tests.Fakes;

/// <summary>
/// Captures what a handler publishes, so a unit test can assert the fact and its
/// payload without a running dispatcher. The bus contract is fire-and-forget;
/// recording is all a publisher can be held to.
/// </summary>
internal sealed class RecordingEventBus : IEventBus
{
    public List<IIntegrationEvent> Published { get; } = [];

    public ValueTask PublishAsync<TEvent>(
        TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        Published.Add(integrationEvent);
        return ValueTask.CompletedTask;
    }
}
