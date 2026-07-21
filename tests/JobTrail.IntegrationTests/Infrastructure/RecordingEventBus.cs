using JobTrail.SharedKernel.Events;

namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Captures what a component publishes, for the tests that drive an application
/// service directly and want to assert the fact it announced without a running
/// dispatcher observing it.
/// </summary>
internal sealed class RecordingEventBus : IEventBus
{
    public List<IIntegrationEvent> Published { get; } = [];

    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        Published.Add(integrationEvent);
        return ValueTask.CompletedTask;
    }
}
