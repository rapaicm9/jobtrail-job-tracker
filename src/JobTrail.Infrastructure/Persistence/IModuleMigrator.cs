namespace JobTrail.Infrastructure.Persistence;

/// <summary>
/// One module's share of bringing the database up to date. Each module registers
/// one of these beside its own <c>DbContext</c>, and the migration one-shot runs
/// every one it can find without knowing which modules exist.
/// <para>
/// The composition twin of the erasure and export fan-outs: implementations stay
/// internal to their module, and the thing that drives them takes
/// <c>IEnumerable&lt;IModuleMigrator&gt;</c> and names none of them. A module added
/// later is migrated because it registered itself, not because a list somewhere
/// was remembered.
/// </para>
/// <para>
/// <b>Nothing migrates on startup.</b> A host that migrated as it came up would
/// have two instances altering one schema during a rolling deploy, so the run is
/// its own process that finishes before either host starts.
/// </para>
/// </summary>
public interface IModuleMigrator
{
    /// <summary>The module's store, for the log line that says what is being migrated.</summary>
    string Store { get; }

    /// <summary>Applies every migration this module's store is missing.</summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}
