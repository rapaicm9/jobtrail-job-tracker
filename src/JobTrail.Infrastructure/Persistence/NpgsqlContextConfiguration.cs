using Microsoft.EntityFrameworkCore;

namespace JobTrail.Infrastructure.Persistence;

/// <summary>
/// The single place that configures a module <see cref="DbContext"/> onto
/// PostgreSQL. Both the runtime DI registration and the design-time factory call
/// this, so the context the app runs and the context migrations are generated
/// against share identical provider and naming configuration - the property that
/// keeps generated migrations honest.
/// </summary>
public static class NpgsqlContextConfiguration
{
    /// <summary>
    /// One history table name across modules; the schema argument keeps each
    /// module's history row in its own schema so modules migrate independently.
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static void Configure(DbContextOptionsBuilder options, string connectionString, string schema)
    {
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema));

        // snake_case in the database, PascalCase in C#.
        options.UseSnakeCaseNamingConvention();
    }
}
