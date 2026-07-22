using JobTrail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTrail.Modules.Applications.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time to build the context when adding
/// or scripting migrations. Shares <see cref="NpgsqlContextConfiguration"/> with
/// the runtime registration so migrations are generated against the exact
/// provider and naming the app runs; the connection here never serves traffic.
/// </summary>
internal sealed class ApplicationsDbContextFactory : IDesignTimeDbContextFactory<ApplicationsDbContext>
{
    // Password-free on purpose: `migrations add` never connects, so it needs only
    // a parseable string, and no credential belongs in tracked source. Applying a
    // migration reads a full connection from JOBTRAIL_DESIGN_TIME_CONNECTION.
    private const string DesignTimeFallback = "Host=localhost;Port=5432;Database=jobtrail;Username=postgres";

    public ApplicationsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("JOBTRAIL_DESIGN_TIME_CONNECTION") ?? DesignTimeFallback;

        var options = new DbContextOptionsBuilder<ApplicationsDbContext>();
        NpgsqlContextConfiguration.Configure(options, connectionString, ApplicationsDbContext.Schema);

        return new ApplicationsDbContext(options.Options);
    }
}
