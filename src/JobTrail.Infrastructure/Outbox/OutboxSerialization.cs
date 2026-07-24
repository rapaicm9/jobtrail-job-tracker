using System.Text.Json;

namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// How an event becomes a stored payload and comes back. One options instance for
/// both directions, because a row written today is read by a process that started
/// weeks later - the two ends must never be configured separately.
/// <para>
/// The defaults are deliberate rather than merely default: property names match
/// the CLR property names, which keeps a stored payload readable next to the
/// record that produced it. This is an internal format, not a public contract - no
/// client ever sees it.
/// </para>
/// </summary>
internal static class OutboxSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    public static string Serialize<TEvent>(TEvent integrationEvent) =>
        JsonSerializer.Serialize(integrationEvent, Options);

    public static TEvent? Deserialize<TEvent>(string payload) =>
        JsonSerializer.Deserialize<TEvent>(payload, Options);
}
