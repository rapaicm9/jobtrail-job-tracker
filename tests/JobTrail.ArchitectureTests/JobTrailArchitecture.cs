using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

// ArchUnitNET.Domain has its own Assembly type modelling a loaded assembly.
// Everything here means the reflection one.
using Assembly = System.Reflection.Assembly;

namespace JobTrail.ArchitectureTests;

/// <summary>
/// The production architecture under test, loaded once for the whole run.
/// </summary>
internal static class JobTrailArchitecture
{
    internal const string SharedKernelAssembly = "JobTrail.SharedKernel";
    internal const string InfrastructureAssembly = "JobTrail.Infrastructure";
    internal const string ApiAssembly = "JobTrail.Api";
    internal const string WorkerAssembly = "JobTrail.Worker";

    /// <summary>Bounded contexts. One module = one schema = one DbContext.</summary>
    internal static readonly string[] Modules =
    [
        "Identity",
        "Applications",
        "Analytics",
        "Notifications",
        "Billing",
    ];

    internal static string ImplementationOf(string module) => $"JobTrail.Modules.{module}";

    internal static string ContractsOf(string module) => $"JobTrail.Modules.{module}.Contracts";

    /// <summary>
    /// Loaded by explicit name rather than by scanning the output directory:
    /// this test assembly references every module in order to load them, so a
    /// directory scan would pull it into the architecture and it would trip the
    /// very rules it is asserting.
    /// </summary>
    private static readonly Dictionary<string, Assembly> LoadedAssemblies =
        new[]
        {
            ApiAssembly,
            WorkerAssembly,
            SharedKernelAssembly,
            InfrastructureAssembly,
        }
        .Concat(Modules.Select(ImplementationOf))
        .Concat(Modules.Select(ContractsOf))
        .ToDictionary(name => name, name => Assembly.Load(name));

    internal static Assembly AssemblyNamed(string name) => LoadedAssemblies[name];

    internal static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies([.. LoadedAssemblies.Values])
        .Build();
}
