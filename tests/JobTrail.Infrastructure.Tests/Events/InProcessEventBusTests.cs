using System.Collections.Concurrent;
using JobTrail.Infrastructure.Events;
using JobTrail.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace JobTrail.Infrastructure.Tests.Events;

/// <summary>
/// Exercises the real bus, the real dispatcher and a real DI container - the
/// behaviour worth pinning here is the dispatcher's isolation guarantees, and a
/// substitute for any of the three would test the substitute instead.
/// <para>
/// Dispatch is asynchronous, so every assertion waits on a signal from the
/// handler rather than sleeping; a bug that stops a handler running surfaces as
/// the wait timing out, not as a flake.
/// </para>
/// </summary>
public sealed class InProcessEventBusTests
{
    /// <summary>Generous: it is a failure detector, never a synchronisation device.</summary>
    private static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Publishing_invokes_every_handler_registered_for_the_event()
    {
        var recorder = new Recorder(expectedCalls: 2);

        await using var harness = await BusHarness.StartAsync(services => services
            .AddSingleton(recorder)
            .AddEventHandler<ThingHappened, FirstHandler>()
            .AddEventHandler<ThingHappened, SecondHandler>());

        await harness.Bus.PublishAsync(new ThingHappened(1), TestContext.Current.CancellationToken);

        await recorder.WaitAsync(HandlerTimeout);
        recorder.Calls.ShouldBe([nameof(FirstHandler), nameof(SecondHandler)], ignoreOrder: true);
    }

    [Fact]
    public async Task Handlers_registered_for_another_event_are_left_alone()
    {
        var recorder = new Recorder(expectedCalls: 1);

        await using var harness = await BusHarness.StartAsync(services => services
            .AddSingleton(recorder)
            .AddEventHandler<ThingHappened, FirstHandler>()
            .AddEventHandler<OtherThingHappened, OtherHandler>());

        await harness.Bus.PublishAsync(new ThingHappened(1), TestContext.Current.CancellationToken);

        await recorder.WaitAsync(HandlerTimeout);
        recorder.Calls.ShouldBe([nameof(FirstHandler)]);
    }

    [Fact]
    public async Task An_event_with_no_handlers_is_dispatched_without_incident()
    {
        var recorder = new Recorder(expectedCalls: 1);

        await using var harness = await BusHarness.StartAsync(services => services
            .AddSingleton(recorder)
            .AddEventHandler<ThingHappened, FirstHandler>());

        // Nothing handles this one. It must neither throw at the publisher nor
        // wedge the dispatcher against the event that follows it.
        await harness.Bus.PublishAsync(new OtherThingHappened(1), TestContext.Current.CancellationToken);
        await harness.Bus.PublishAsync(new ThingHappened(2), TestContext.Current.CancellationToken);

        await recorder.WaitAsync(HandlerTimeout);
        recorder.Calls.ShouldBe([nameof(FirstHandler)]);
    }

    [Fact]
    public async Task A_throwing_handler_does_not_deprive_the_others_of_the_event()
    {
        var recorder = new Recorder(expectedCalls: 2);

        await using var harness = await BusHarness.StartAsync(services => services
            .AddSingleton(recorder)
            .AddEventHandler<ThingHappened, ThrowingHandler>()
            .AddEventHandler<ThingHappened, FirstHandler>());

        await harness.Bus.PublishAsync(new ThingHappened(1), TestContext.Current.CancellationToken);

        await recorder.WaitAsync(HandlerTimeout);
        recorder.Calls.ShouldContain(nameof(FirstHandler));
    }

    [Fact]
    public async Task A_throwing_handler_does_not_take_the_dispatcher_down()
    {
        // The failure this guards against is the quiet one: one bad handler
        // ending the dispatch loop stops every reaction in the process, and
        // nothing about the next lost event says why.
        //
        // Both handlers run for both events, so the wait must expect all four
        // calls - releasing on three would let the assertion read the counts
        // while the last handler was still running.
        var recorder = new Recorder(expectedCalls: 4);

        await using var harness = await BusHarness.StartAsync(services => services
            .AddSingleton(recorder)
            .AddEventHandler<ThingHappened, ThrowingHandler>()
            .AddEventHandler<ThingHappened, FirstHandler>());

        await harness.Bus.PublishAsync(new ThingHappened(1), TestContext.Current.CancellationToken);
        await harness.Bus.PublishAsync(new ThingHappened(2), TestContext.Current.CancellationToken);

        await recorder.WaitAsync(HandlerTimeout);
        recorder.Calls.Count(call => call == nameof(FirstHandler)).ShouldBe(2);
    }

    [Fact]
    public async Task Each_event_is_handled_in_a_scope_of_its_own()
    {
        var recorder = new Recorder(expectedCalls: 2);

        await using var harness = await BusHarness.StartAsync(services => services
            .AddSingleton(recorder)
            .AddEventHandler<ThingHappened, ScopeProbeHandler>());

        await harness.Bus.PublishAsync(new ThingHappened(1), TestContext.Current.CancellationToken);
        await harness.Bus.PublishAsync(new ThingHappened(2), TestContext.Current.CancellationToken);

        await recorder.WaitAsync(HandlerTimeout);

        // A scoped handler resolved twice from one scope would be one instance;
        // two distinct ids prove the dispatcher opened a scope per event, which
        // is what lets a handler take a scoped DbContext.
        recorder.Calls.Distinct().Count().ShouldBe(2);
    }

    private sealed record ThingHappened(int Id) : IIntegrationEvent;

    private sealed record OtherThingHappened(int Id) : IIntegrationEvent;

    /// <summary>
    /// Collects what ran and releases the test once the expected number of
    /// handler calls have landed.
    /// </summary>
    private sealed class Recorder
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expectedCalls;
        private int _outstanding;

        public Recorder(int expectedCalls)
        {
            _expectedCalls = expectedCalls;
            _outstanding = expectedCalls;
        }

        public IReadOnlyCollection<string> Calls => _calls;

        public void Record(string call)
        {
            _calls.Enqueue(call);

            if (Interlocked.Decrement(ref _outstanding) == 0)
            {
                _reached.TrySetResult();
            }
        }

        public async Task WaitAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_reached.Task, Task.Delay(timeout));

            completed.ShouldBe(
                _reached.Task,
                $"expected {_expectedCalls} handler call(s) within {timeout.TotalSeconds:N0}s, "
                + $"saw: {string.Join(", ", _calls)}");
        }
    }

    private sealed class FirstHandler(Recorder recorder) : IEventHandler<ThingHappened>
    {
        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Record(nameof(FirstHandler));

            return Task.CompletedTask;
        }
    }

    private sealed class SecondHandler(Recorder recorder) : IEventHandler<ThingHappened>
    {
        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Record(nameof(SecondHandler));

            return Task.CompletedTask;
        }
    }

    private sealed class OtherHandler(Recorder recorder) : IEventHandler<OtherThingHappened>
    {
        public Task HandleAsync(OtherThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Record(nameof(OtherHandler));

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler(Recorder recorder) : IEventHandler<ThingHappened>
    {
        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Record(nameof(ThrowingHandler));

            throw new InvalidOperationException("This handler is meant to fail.");
        }
    }

    /// <summary>Records its own identity, so two calls reveal whether they shared a scope.</summary>
    private sealed class ScopeProbeHandler(Recorder recorder) : IEventHandler<ThingHappened>
    {
        private readonly string _instanceId = Guid.CreateVersion7().ToString();

        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            recorder.Record(_instanceId);

            return Task.CompletedTask;
        }
    }

    /// <summary>A composed container with the dispatcher actually running.</summary>
    private sealed class BusHarness(ServiceProvider provider, IHostedService dispatcher) : IAsyncDisposable
    {
        public IEventBus Bus => provider.GetRequiredService<IEventBus>();

        public static async Task<BusHarness> StartAsync(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInProcessEventBus();
            configure(services);

            var provider = services.BuildServiceProvider();
            var dispatcher = provider.GetServices<IHostedService>().Single();
            await dispatcher.StartAsync(CancellationToken.None);

            return new BusHarness(provider, dispatcher);
        }

        public async ValueTask DisposeAsync()
        {
            await dispatcher.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
