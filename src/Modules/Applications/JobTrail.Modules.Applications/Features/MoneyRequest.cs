namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// A compensation amount paired with its currency code, as a client sends it on
/// create or update. Shared by both write slices so the wire shape is one thing.
/// </summary>
internal sealed record MoneyRequest(decimal Amount, string? Currency);
