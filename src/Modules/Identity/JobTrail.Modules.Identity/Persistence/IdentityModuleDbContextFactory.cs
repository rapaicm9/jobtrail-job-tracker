using JobTrail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTrail.Modules.Identity.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time to build the context when adding
/// or scripting migrations. It shares <see cref="NpgsqlContextConfiguration"/>
/// with the runtime registration, so migrations are generated against the exact
/// provider and naming the app runs. The connection here is never used to serve
/// traffic - the AppHost injects the real one at runtime.
/// </summary>
internal sealed class IdentityModuleDbContextFactory : IDesignTimeDbContextFactory<IdentityModuleDbContext>
{
    // Password-free on purpose: `migrations add` never connects, so it needs
    // only a parseable string, and no credential belongs in tracked source.
    // Applying a migration (`database update`) reads a full connection - with
    // credentials - from JOBTRAIL_DESIGN_TIME_CONNECTION, set in the shell.
    private const string DesignTimeFallback = "Host=localhost;Port=5432;Database=jobtrail;Username=postgres";

    public IdentityModuleDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("JOBTRAIL_DESIGN_TIME_CONNECTION") ?? DesignTimeFallback;

        var options = new DbContextOptionsBuilder<IdentityModuleDbContext>();
        NpgsqlContextConfiguration.Configure(options, connectionString, IdentityModuleDbContext.Schema);

        return new IdentityModuleDbContext(options.Options);
    }
}
