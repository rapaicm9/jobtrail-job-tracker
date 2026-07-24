using JobTrail.SharedKernel.Events;

namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// One integration event recorded for durable delivery. A publishing module adds
/// the row in the same <c>SaveChanges</c> as the state change it describes, so the
/// fact and the announcement of it commit together or not at all - the point of
/// the whole mechanism. A dispatcher then delivers it and marks it processed.
/// <para>
/// The row belongs to the publishing module's schema, not to Infrastructure: it
/// has to share the module's transaction, which means sharing its
/// <c>DbContext</c>. Infrastructure only supplies the shape and the dispatcher.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Shared by the column mapping and the dispatcher, so a recorded failure always fits.</summary>
    public const int MaxErrorLength = 500;

    /// <summary>Long enough for a readable event name, short enough to stay an identifier.</summary>
    public const int MaxEventTypeLength = 100;

    private OutboxMessage(string eventType, string payload)
    {
        EventType = eventType;
        Payload = payload;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The stable name the event was registered under - deliberately not the CLR
    /// type name, so renaming a record cannot orphan rows already written.
    /// </summary>
    public string EventType { get; private set; }

    /// <summary>The serialized event. Opaque here; only the registered type knows its shape.</summary>
    public string Payload { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Set once every handler has run; null while the event is still owed.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>How many deliveries have failed. Past a threshold the row is left alone for a human.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the next delivery may be attempted; null means immediately.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Why the last delivery failed, kept for diagnosis; cleared on success.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Records an event for delivery. The id and the occurred-at come from the
    /// database, so a row is never written with a clock this process guessed.
    /// </summary>
    public static OutboxMessage For<TEvent>(string eventType, TEvent integrationEvent)
        where TEvent : IIntegrationEvent =>
        new(eventType, OutboxSerialization.Serialize(integrationEvent));

    internal void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        Error = null;
        NextAttemptAt = null;
    }

    internal void RecordFailure(string error, DateTimeOffset retryAt)
    {
        Attempts++;
        Error = error;
        NextAttemptAt = retryAt;
    }
}
