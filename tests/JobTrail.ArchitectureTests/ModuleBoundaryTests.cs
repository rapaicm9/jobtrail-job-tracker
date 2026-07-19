using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static JobTrail.ArchitectureTests.JobTrailArchitecture;

namespace JobTrail.ArchitectureTests;

/// <summary>
/// Executable form of the module boundary rules. A violation here is a failed
/// build, not a review comment: if a change appears to require breaking one of
/// these, the design is wrong, not the test.
/// </summary>
public sealed class ModuleBoundaryTests
{
    /// <summary>Every ordered pair of distinct modules.</summary>
    public static TheoryData<string, string> ModulePairs
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var module in ModuleNames)
            {
                foreach (var other in ModuleNames.Where(m => m != module))
                {
                    data.Add(module, other);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> AllModules => [.. ModuleNames];

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Module_must_not_depend_on_another_modules_implementation(string module, string other)
    {
        if (!AssemblyHasTypes(ImplementationOf(module)))
        {
            Assert.Skip($"{module} carries no types yet; this rule turns live once it does.");
        }

        Types()
            .That()
            .ResideInAssembly(AssemblyNamed(ImplementationOf(module)))
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(AssemblyNamed(ImplementationOf(other))))
            .Because(
                $"{module} and {other} are separate bounded contexts; they may only meet through "
                + $"a Contracts project, never by reaching into each other's internals.")
            .Check(Architecture);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Contracts_must_not_depend_on_any_module_implementation(string module)
    {
        if (!AssemblyHasTypes(ContractsOf(module)))
        {
            Assert.Skip($"{module}.Contracts carries no types yet; this rule turns live once it does.");
        }

        var implementations = ModuleNames.Select(m => AssemblyNamed(ImplementationOf(m))).ToArray();

        Types()
            .That()
            .ResideInAssembly(AssemblyNamed(ContractsOf(module)))
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(implementations[0], implementations[1..]))
            .Because(
                "a Contracts project is the module's public surface and must be safe for any "
                + "other module to reference; depending on an implementation would drag internals "
                + "across the boundary and defeat the point.")
            .Check(Architecture);
    }

    [Theory]
    [MemberData(nameof(AllModules))]
    public void Module_must_not_depend_on_a_host(string module)
    {
        if (!AssemblyHasTypes(ImplementationOf(module)))
        {
            Assert.Skip($"{module} carries no types yet; this rule turns live once it does.");
        }

        Types()
            .That()
            .ResideInAssembly(AssemblyNamed(ImplementationOf(module)))
            .Should()
            .NotDependOnAny(
                Types()
                    .That()
                    .ResideInAssembly(AssemblyNamed(ApiAssembly), AssemblyNamed(WorkerAssembly)))
            .Because(
                "dependencies point inward: hosts compose modules, modules never reach back. "
                + "A module that knows its host cannot be extracted or reused by the other host.")
            .Check(Architecture);
    }

    [Fact]
    public void SharedKernel_must_not_depend_on_anything_else_in_the_solution()
    {
        var everythingElse = ModuleNames
            .Select(ImplementationOf)
            .Concat(ModuleNames.Select(ContractsOf))
            .Concat([InfrastructureAssembly, ApiAssembly, WorkerAssembly])
            .Select(AssemblyNamed)
            .ToArray();

        Types()
            .That()
            .ResideInAssembly(AssemblyNamed(SharedKernelAssembly))
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(everythingElse[0], everythingElse[1..]))
            .Because(
                "the shared kernel holds primitives only - strongly-typed ids, Result/Error, "
                + "Money, event abstractions, the clock. Anything it depends on becomes shared "
                + "by every module whether they wanted it or not.")
            .Check(Architecture);
    }
}
