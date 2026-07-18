using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static JobTrail.ArchitectureTests.JobTrailArchitecture;

namespace JobTrail.ArchitectureTests;

/// <summary>
/// Rules keeping each module's storage private to it.
/// <para>
/// Rules of the form "X must not depend on &lt;external library&gt;" are absent
/// here on purpose. The rule engine only sees types from assemblies loaded into
/// the architecture, so a rule naming a library that nothing references yet
/// matches zero types and passes - and would keep passing after the library
/// arrived, since it still would not be loaded. Those rules land with the
/// library, where the assembly can be loaded and the rule proven to fail on a
/// real violation. See ADR-0007.
/// </para>
/// </summary>
public sealed class PersistenceBoundaryTests
{
    public static TheoryData<string> AllModules => [.. ModuleNames];

    [Theory]
    [MemberData(nameof(AllModules))]
    public void DbContext_must_not_be_reachable_outside_its_own_module(string module)
    {
        var outsideThisModule = ModuleNames
            .Where(m => m != module)
            .Select(ImplementationOf)
            .Concat([ApiAssembly, WorkerAssembly, InfrastructureAssembly])
            .Select(AssemblyNamed)
            .ToArray();

        Types()
            .That()
            .ResideInAssembly(outsideThisModule[0], outsideThisModule[1..])
            .Should()
            .NotDependOnAny(
                Types()
                    .That()
                    .ResideInAssembly(AssemblyNamed(ImplementationOf(module)))
                    .And()
                    .HaveNameEndingWith("DbContext"))
            .Because(
                "a module owns its schema outright. Reaching another module's DbContext is how a "
                + "cross-schema query gets written, and a cross-schema query is a boundary "
                + "violation whether or not it happens to compile.")
            .Check(Architecture);
    }
}
