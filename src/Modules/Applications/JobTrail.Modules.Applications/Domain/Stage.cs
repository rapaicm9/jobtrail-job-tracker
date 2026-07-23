namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// Where an application sits in the pipeline. The first four are <em>active</em>
/// and strictly ordered - a live application advances through them (skips
/// allowed). The last four are <em>terminal</em> outcomes and carry no order
/// among themselves. The split, not the declaration order, is what the state
/// machine reasons about (see <see cref="StageExtensions"/>); the ordering of the
/// active values is load-bearing, though, since a forward move must land on a
/// strictly later one. Stored as its name, so the column reads for itself and a
/// future stage never depends on ordinal stability.
/// </summary>
internal enum Stage
{
    // Active, ordered: the live pipeline.
    Applied,
    Screening,
    Interview,
    Offer,

    // Terminal, unordered: the outcomes an application ends on.
    Accepted,
    Rejected,
    Withdrawn,
    Ghosted,
}
