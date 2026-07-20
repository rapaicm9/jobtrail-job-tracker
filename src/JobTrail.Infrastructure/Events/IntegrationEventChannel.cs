using System.Threading.Channels;

namespace JobTrail.Infrastructure.Events;

/// <summary>
/// The in-memory queue between publishers and the dispatcher, wrapped so the
/// channel is registered as one singleton with a name rather than as a bare
/// generic type both ends have to spell identically.
/// </summary>
internal sealed class IntegrationEventChannel
{
    /// <summary>
    /// Bounded on purpose. An unbounded queue turns a stalled dispatcher into
    /// unbounded memory growth and an eventual OOM with no earlier symptom;
    /// bounded, the same stall shows up as publishers slowing down, which is
    /// visible, survivable, and points straight at the cause. The capacity is
    /// far above any burst a single-instance API can produce, so reaching it
    /// means the dispatcher is genuinely stuck.
    /// </summary>
    private const int Capacity = 1024;

    private readonly Channel<IntegrationEventEnvelope> _channel =
        Channel.CreateBounded<IntegrationEventEnvelope>(
            new BoundedChannelOptions(Capacity)
            {
                // One dispatcher reads; any request thread may write.
                SingleReader = true,
                SingleWriter = false,

                // Wait rather than drop: losing an event silently is the one
                // failure mode that would be invisible in production.
                FullMode = BoundedChannelFullMode.Wait,
            });

    public ChannelReader<IntegrationEventEnvelope> Reader => _channel.Reader;

    public ChannelWriter<IntegrationEventEnvelope> Writer => _channel.Writer;
}
