using JobTrail.Infrastructure.Outbox;
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

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Interview> Interviews => Set<Interview>();

    /// <summary>
    /// Events this module owes other modules. It lives here, in this module's
    /// store, so a row is written in the same transaction as the change it
    /// announces - which is the only reason the outbox works at all.
    /// </summary>
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

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

            // A user's applications, read back by owner in the list's own order -
            // the access path every ownership-scoped query takes, and the one the
            // cursor walks. Ascending: the list reads newest-first, which Postgres
            // serves by scanning this backwards. Owner alone is the leading column,
            // so plain "this user's applications" is served too.
            application.HasIndex(a => new { a.OwnerId, a.AppliedDate, a.Id });

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

            // A manual note's text; null on the automatic entries.
            entry.Property(e => e.Note).HasMaxLength(2000);

            // A child of its application: deleting the application takes its whole
            // timeline with it, so no orphan rows survive.
            entry.HasOne<Application>()
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Read back by application in the timeline's own order, and by owner
            // for erasure. The timeline reads newest-first off a backward scan.
            entry.HasIndex(e => new { e.ApplicationId, e.CreatedAt, e.Id });
            entry.HasIndex(e => e.OwnerId);
        });

        builder.Entity<Contact>(contact =>
        {
            contact.HasKey(c => c.Id);
            contact.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            contact.Property(c => c.CreatedAt).HasDefaultValueSql("now()");

            contact.Property(c => c.Name).HasMaxLength(200).IsRequired();
            contact.Property(c => c.Role).HasConversion<string>().HasMaxLength(32);
            contact.Property(c => c.Email).HasMaxLength(320);
            contact.Property(c => c.Phone).HasMaxLength(40);
            contact.Property(c => c.Notes).HasMaxLength(2000);

            // Linked to an application and/or a company (the slice requires at
            // least one). Same-schema FKs: the application link is cascade-deleted
            // with its application, the company link nulls out if the company goes.
            contact.HasOne<Application>()
                .WithMany()
                .HasForeignKey(c => c.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            contact.HasOne<Company>()
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.SetNull);

            // Read back by owner in the list's own order - name, then id to break
            // the ties names have - and by each link for the filtered list and the
            // foreign keys.
            contact.HasIndex(c => new { c.OwnerId, c.Name, c.Id });
            contact.HasIndex(c => c.ApplicationId);
            contact.HasIndex(c => c.CompanyId);
        });

        builder.Entity<Interview>(interview =>
        {
            interview.HasKey(i => i.Id);
            interview.Property(i => i.Id).HasDefaultValueSql("uuidv7()");
            interview.Property(i => i.CreatedAt).HasDefaultValueSql("now()");

            interview.Property(i => i.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
            interview.Property(i => i.Format).HasConversion<string>().HasMaxLength(16).IsRequired();
            interview.Property(i => i.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
            interview.Property(i => i.Notes).HasMaxLength(2000);

            // A child of its application: deleting the application takes its rounds
            // with it. The application link is required - a round has no meaning
            // apart from the application it is on.
            interview.HasOne<Application>()
                .WithMany()
                .HasForeignKey(i => i.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Read back by application in the round list's own order, and by owner
            // for erasure.
            interview.HasIndex(i => new { i.ApplicationId, i.ScheduledAt, i.Id });
            interview.HasIndex(i => i.OwnerId);
        });

        builder.Entity<OutboxMessage>(message =>
        {
            // "outbox" rather than the convention's plural: it is one queue, and
            // that is what it is called everywhere it is discussed.
            message.ToTable("outbox");

            message.HasKey(m => m.Id);
            message.Property(m => m.Id).HasDefaultValueSql("uuidv7()");
            message.Property(m => m.OccurredAt).HasDefaultValueSql("now()");

            message.Property(m => m.EventType).HasMaxLength(OutboxMessage.MaxEventTypeLength).IsRequired();
            message.Property(m => m.Error).HasMaxLength(OutboxMessage.MaxErrorLength);

            // jsonb rather than text: the payload is a document, and storing it as
            // one keeps it queryable when a delivery has to be explained.
            message.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();

            // The dispatcher only ever reads what is still owed, in the order it
            // was recorded. A partial index keeps that access path the size of the
            // backlog rather than the size of the history.
            message.HasIndex(m => new { m.OccurredAt, m.Id }).HasFilter("processed_at IS NULL");
        });
    }
}
