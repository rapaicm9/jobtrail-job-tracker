using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Jobspect.ArchitectureTests.JobspectArchitecture;

namespace Jobspect.ArchitectureTests;

/// <summary>
/// The scheduler is one module's business. Stated here rather than left to
/// convention, and stated now rather than earlier, for the reason
/// <see cref="PersistenceBoundaryTests"/> gives: a rule naming a library that
/// nothing references matches nothing and passes vacuously forever. Quartz has
/// just arrived, so the rule can be written and proven to fail on a real
/// violation.
/// </summary>
public sealed class SchedulerBoundaryTests
{
    /// <summary>
    /// Every assembly that is not the module that owns the schedule. The worker is
    /// deliberately in here: it composes the scheduler through the module's own
    /// registration method and must never name a Quartz type itself, which is what
    /// keeps "the jobs are thin and the host only composes" true rather than
    /// merely intended.
    /// </summary>
    public static TheoryData<string> EverythingButNotifications =>
    [
        .. ModuleNames.Where(m => m != "Notifications").Select(ImplementationOf),
        .. ModuleNames.Select(ContractsOf),
        .. HostAssemblies,
        SharedKernelAssembly,
        InfrastructureAssembly,
    ];

    [Theory]
    [MemberData(nameof(EverythingButNotifications))]
    public void Only_the_notifications_module_knows_about_the_scheduler(string assembly)
    {
        if (!AssemblyHasTypes(assembly))
        {
            Assert.Skip($"{assembly} carries no types yet; this rule turns live once it does.");
        }

        Types()
            .That()
            .ResideInAssembly(AssemblyNamed(assembly))
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(AssemblyNamed(SchedulerAssembly)))
            .Because(
                "scheduling is the Notifications module's own concern: it owns the jobs, and the "
                + "job store's tables live in its schema. A host that named a scheduler type would "
                + "be doing the module's work, and another module that did would be scheduling "
                + "into a store it does not own.")
            .Check(Architecture);
    }
}
