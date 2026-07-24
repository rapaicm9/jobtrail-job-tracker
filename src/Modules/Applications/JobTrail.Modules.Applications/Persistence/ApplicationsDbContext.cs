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

    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();

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

            // Built-in fields: relational columns, because everything here is
            // filtered, sorted or charted on - that's what keeps them first-class.
            // The custom-field remainder lives in a JSONB bag later.
            application.Property(a => a.Role).HasMaxLength(200).IsRequired();
            application.Property(a => a.Location).HasMaxLength(200);
            application.Property(a => a.PostingUrl).HasMaxLength(2048);
            application.Property(a => a.Source).HasMaxLength(100);
            application.Property(a => a.CvLabel).HasMaxLength(200);
            application.Property(a => a.CoverLetterLabel).HasMaxLength(200);

            application.Property(a => a.WorkMode)
                .HasConversion<string>()
                .HasMaxLength(16);

            // Applied-at defaults to the current date at the database; the create
            // slice overrides it with the user's local "today" (dates are
            // timezone-relative), but a row is never left without one.
            application.Property(a => a.AppliedDate).HasDefaultValueSql("CURRENT_DATE");

            // Compensation is an amount + currency that travel together, so it maps
            // as one optional complex type over two columns; both are null when the
            // user hasn't recorded any.
            application.ComplexProperty(a => a.Compensation, compensation =>
            {
                compensation.IsRequired(false);
                compensation.Property(m => m.Amount).HasPrecision(19, 4);
                compensation.Property(m => m.Currency).HasMaxLength(3);
            });

            // Campaign is the application's required home; a restricted delete stops
            // a campaign that still holds applications from being removed out from
            // under them. Company is optional and nulls out if the company is
            // deleted, leaving the application. Both are same-schema FKs - the
            // cross-schema ban is about other modules, not within this one.
            application.HasOne<Campaign>()
                .WithMany()
                .HasForeignKey(a => a.CampaignId)
                .OnDelete(DeleteBehavior.Restrict);

            application.HasOne<Company>()
                .WithMany()
                .HasForeignKey(a => a.CompanyId)
                .OnDelete(DeleteBehavior.SetNull);

            // A user's applications, read back by owner - the access path every
            // ownership-scoped query takes. Non-unique: a user has many.
            application.HasIndex(a => a.OwnerId);

            // Support the foreign keys and the "applications in this campaign / at
            // this company" reads that follow them.
            application.HasIndex(a => a.CampaignId);
            application.HasIndex(a => a.CompanyId);
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

        builder.Entity<ActivityLogEntry>(entry =>
        {
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
            entry.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            // The kind and the two stage ends store as their names, like the
            // application's own stage - the timeline reads for itself.
            entry.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            entry.Property(e => e.FromStage).HasConversion<string>().HasMaxLength(16);
            entry.Property(e => e.ToStage).HasConversion<string>().HasMaxLength(16);
            entry.Property(e => e.TransitionKind).HasConversion<string>().HasMaxLength(16);

            // A child of its application: deleting the application takes its whole
            // timeline with it, so no orphan rows survive.
            entry.HasOne<Application>()
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Read back by application (the timeline) and by owner (erasure).
            entry.HasIndex(e => e.ApplicationId);
            entry.HasIndex(e => e.OwnerId);
        });
    }
}
