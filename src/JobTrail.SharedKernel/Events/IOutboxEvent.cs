namespace JobTrail.SharedKernel.Events;

/// <summary>
/// An integration event durable enough to be recorded before it is delivered -
/// one whose loss would cost a consumer a fact it can never recover, since it
/// cannot read the publishing module's tables to catch up.
/// <para>
/// The name its stored rows carry is declared on the event type itself. The
/// publisher and the reader therefore cannot disagree about it: a row written
/// under a name nothing was registered under is a row nobody can deliver, and
/// that mistake is now a compile-time impossibility rather than a runtime
/// surprise. The name is still <em>declared</em> rather than derived from the
/// type name, so renaming the record never orphans rows already written.
/// </para>
/// </summary>
public interface IOutboxEvent : IIntegrationEvent
{
    /// <summary>The stable name this event's stored rows carry.</summary>
    static abstract string EventType { get; }
}
