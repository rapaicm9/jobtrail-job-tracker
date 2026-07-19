using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

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

    /// <summary>
    /// Bounded contexts. One module = one schema = one DbContext.
    /// Named <c>ModuleNames</c> rather than <c>Modules</c> so it is not shadowed
    /// by the <c>JobTrail.Modules</c> namespace, which becomes visible here once
    /// a module carries real types.
    /// </summary>
    internal static readonly string[] ModuleNames =
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
        .Concat(ModuleNames.Select(ImplementationOf))
        .Concat(ModuleNames.Select(ContractsOf))
        .ToDictionary(name => name, name => Assembly.Load(name));

    internal static Assembly AssemblyNamed(string name) => LoadedAssemblies[name];

    internal static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies([.. LoadedAssemblies.Values])
        .Build();

    /// <summary>
    /// Whether the named assembly contributes any types to the architecture,
    /// resolved through the same predicate a rule would use for its subject.
    /// <para>
    /// A "must-not-depend" rule whose subject assembly is still empty matches
    /// zero types, and ArchUnitNET fails such a rule for want of a positive
    /// result rather than passing it. The boundary rules guard on this so an
    /// empty module skips vacuously and the same rule turns live, unweakened,
    /// the moment that module carries its first real type. See ADR-0007.
    /// </para>
    /// </summary>
    internal static bool AssemblyHasTypes(string name) =>
        Types().That().ResideInAssembly(AssemblyNamed(name)).GetObjects(Architecture).Any();
}
