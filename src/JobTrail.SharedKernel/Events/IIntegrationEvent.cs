namespace JobTrail.SharedKernel.Events;

/// <summary>
/// Marks a fact that one module publishes for others to react to. An
/// integration event describes something that has already happened, so it is
/// named in the past tense and carries only ids and values - never an entity,
/// which would drag a module's domain across the boundary.
/// <para>
/// Implementations are records living in the publishing module's Contracts
/// project: the event is part of that module's public surface, and a consumer
/// must be able to reference it without referencing the implementation.
/// </para>
/// </summary>
public interface IIntegrationEvent;
