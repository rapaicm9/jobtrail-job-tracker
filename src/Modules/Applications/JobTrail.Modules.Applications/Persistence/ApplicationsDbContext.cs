using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Domain;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Persistence;

/// <summary>
/// The Applications module's private store, inside its own <c>applications</c>
/// schema. This slice holds only the application aggregate's skeleton; the
/// pipeline, built-in fields, campaigns, companies, contacts and interviews land
/// in later slices, each in this same schema.
/// </summary>
internal sealed class ApplicationsDbContext(DbContextOptions<ApplicationsDbContext> options) : DbContext(options)
{
    public const string Schema = "applications";

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<Company> Companies => Set<Company>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        // Owner columns carry the strongly-typed id and store as uuid; one place,
        // so no property has to remember to opt in.
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        builder.Entity<Application>(application =>
        {
            application.HasKey(a => a.Id);
            application.Property(a => a.Id).HasDefaultValueSql("uuidv7()");
            application.Property(a => a.CreatedAt).HasDefaultValueSql("now()");

            // Stored as its name, and defaulted at the database so a freshly
            // inserted application lands on Applied without the code saying so.
            application.Property(a => a.Stage)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired()
                .HasDefaultValue(Stage.Applied);

            // A user's applications, read back by owner - the access path every
            // ownership-scoped query takes. Non-unique: a user has many.
            application.HasIndex(a => a.OwnerId);
        });

        builder.Entity<Campaign>(campaign =>
        {
            campaign.HasKey(c => c.Id);
            campaign.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            campaign.Property(c => c.Name).HasMaxLength(100).IsRequired();
            campaign.Property(c => c.CreatedAt).HasDefaultValueSql("now()");

            // Exactly one default campaign per user, enforced at the database. A
            // partial (filtered) unique index constrains only the default rows, so
            // the extra campaigns a Pro account adds stay unconstrained - and it
            // doubles as the access path for "this user's default".
            campaign.HasIndex(c => c.OwnerId)
                .IsUnique()
                .HasFilter("is_default");
        });

        builder.Entity<Company>(company =>
        {
            company.HasKey(c => c.Id);
            company.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            company.Property(c => c.Name).HasMaxLength(200).IsRequired();
            company.Property(c => c.Website).HasMaxLength(2048);
            company.Property(c => c.Notes).HasMaxLength(2000);
            company.Property(c => c.CreatedAt).HasDefaultValueSql("now()");

            // A user's companies, read back by owner for the type-ahead picker.
            company.HasIndex(c => c.OwnerId);
        });
    }
}
