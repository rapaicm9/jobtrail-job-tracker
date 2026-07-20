using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobTrail.Infrastructure.Events;

/// <summary>
/// Drains the event queue for the life of the host, running each event's
/// handlers in a scope of its own.
/// <para>
/// The dispatcher never lets a handler take it down. A handler that throws is
/// logged and its event moves on, because the alternative - a dead dispatcher -
/// silently stops every reaction in the process rather than the one that
/// failed. Nothing retries, so a handler that must not lose its work belongs
/// behind the outbox instead.
/// </para>
/// <para>
/// Events still queued when the host stops are dropped. That is the accepted
/// cost of in-memory dispatch and the reason the reminder-bearing events will
/// be published transactionally rather than through this path.
/// </para>
/// </summary>
internal sealed partial class IntegrationEventDispatcher(
    IntegrationEventChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<IntegrationEventDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var envelope in channel.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(envelope, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown, not a fault.
        }
    }

    private async Task DispatchAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        // One scope per event, not per handler: in practice an event's handlers
        // live in different modules and so resolve different DbContexts, and a
        // shared scope keeps a single event's work on a single unit of work.
        await using var scope = scopeFactory.CreateAsyncScope();

        IReadOnlyList<HandlerInvocation> handlers;

        try
        {
            handlers = envelope.ResolveHandlers(scope.ServiceProvider);
        }
        catch (Exception exception)
        {
            // A registration mistake, not a handler fault - the event is lost
            // either way, but the two read very differently at 3am.
            HandlerResolutionFailed(envelope.EventName, exception);
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler.InvokeAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                HandlerFailed(handler.HandlerName, envelope.EventName, exception);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not resolve handlers for integration event {EventName}; the event was dropped.")]
    private partial void HandlerResolutionFailed(string eventName, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Handler {HandlerName} failed for integration event {EventName}; the event was dropped for "
            + "this handler only.")]
    private partial void HandlerFailed(string handlerName, string eventName, Exception exception);
}
