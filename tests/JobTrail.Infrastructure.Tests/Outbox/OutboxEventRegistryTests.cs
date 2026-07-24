using JobTrail.Infrastructure.Outbox;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.Infrastructure.Tests.Outbox;

/// <summary>
/// The registry is the whole path a stored event takes back into typed code:
/// recorded payload in, handlers run out. The behaviour worth pinning is that the
/// round trip is faithful, and that a failure gets <b>out</b> - the dispatcher
/// only knows an event is still owed because delivery threw at it.
/// </summary>
public sealed class OutboxEventRegistryTests
{
    private const string EventType = "tests.thing_happened";

    [Fact]
    public async Task Delivers_the_recorded_event_to_every_handler()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var provider = BuildProvider(first, second);
        var registry = new OutboxEventRegistry().Register<ThingHappened>(EventType);
        var message = Record(new ThingHappened(UserId.New(), "a note", new DateOnly(2026, 7, 24), 3));

        await registry.DeliverAsync(EventType, message.Payload, provider, TestContext.Current.CancellationToken);

        first.Received.ShouldHaveSingleItem();
        second.Received.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Round_trips_the_payload_including_a_strongly_typed_id()
    {
        var handler = new RecordingHandler();
        var provider = BuildProvider(handler);
        var registry = new OutboxEventRegistry().Register<ThingHappened>(EventType);
        var original = new ThingHappened(UserId.New(), "a note", new DateOnly(2026, 7, 24), 3);

        await registry.DeliverAsync(
            EventType, Record(original).Payload, provider, TestContext.Current.CancellationToken);

        // Every member survives, UserId included - it is a record struct, so it
        // travels as its wrapped value and comes back through the same constructor.
        handler.Received.ShouldHaveSingleItem().ShouldBe(original);
    }

    [Fact]
    public async Task Lets_a_handler_failure_out()
    {
        var provider = BuildProvider(new ThrowingHandler());
        var registry = new OutboxEventRegistry().Register<ThingHappened>(EventType);
        var message = Record(new ThingHappened(UserId.New(), "a note", new DateOnly(2026, 7, 24), 3));

        // Swallowing this is what would make the outbox a lie: the row would be
        // marked processed although nothing handled it.
        await Should.ThrowAsync<InvalidOperationException>(() => registry.DeliverAsync(
            EventType, message.Payload, provider, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Refuses_an_event_name_it_does_not_know()
    {
        var registry = new OutboxEventRegistry();

        // A stored row nobody can read is a deployment mistake. It has to surface
        // as a failure, not as a quietly discarded event.
        await Should.ThrowAsync<InvalidOperationException>(() => registry.DeliverAsync(
            "tests.never_registered", "{}", BuildProvider(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Knows_which_events_it_can_deliver()
    {
        var registry = new OutboxEventRegistry().Register<ThingHappened>(EventType);

        registry.IsRegistered(EventType).ShouldBeTrue();
        registry.IsRegistered("tests.never_registered").ShouldBeFalse();
    }

    private static OutboxMessage Record(ThingHappened integrationEvent) =>
        OutboxMessage.For(EventType, integrationEvent);

    private static IServiceProvider BuildProvider(params IEventHandler<ThingHappened>[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        return services.BuildServiceProvider();
    }

    private sealed record ThingHappened(UserId OwnerId, string Note, DateOnly On, int Count) : IIntegrationEvent;

    private sealed class RecordingHandler : IEventHandler<ThingHappened>
    {
        public List<ThingHappened> Received { get; } = [];

        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            Received.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IEventHandler<ThingHappened>
    {
        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the handler could not do its work");
    }
}
