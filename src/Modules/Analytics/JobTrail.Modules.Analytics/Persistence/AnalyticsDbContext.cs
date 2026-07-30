using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Analytics.Domain;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Persistence;

/// <summary>
/// The Analytics module's private store, inside its own <c>analytics</c> schema:
/// one base row per application, from which every figure the dashboard shows is
/// aggregated on read.
/// <para>
/// There is no outbox here, and its absence is deliberate rather than an omission
/// - every other module's context maps one. Analytics is a read model. It
/// consumes events and publishes none, so it has nothing to owe anyone.
/// </para>
/// </summary>
internal sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public const string Schema = "analytics";

    public DbSet<ApplicationFacts> ApplicationFacts => Set<ApplicationFacts>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        // Owner columns carry the strongly-typed id and store as uuid; one place,
        // so no property has to remember to opt in.
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationFacts>(facts =>
        {
            // Keyed by the application the events are about, and deliberately
            // without the uuidv7() default every other table in this system
            // carries: the id arrives on the event and is never minted here. That
            // is what lets every projection be a plain upsert on this key.
            facts.HasKey(f => f.ApplicationId);
            facts.Property(f => f.ApplicationId).ValueGeneratedNever();

            facts.Property(f => f.CreatedAt).HasDefaultValueSql("now()");

            // Stage names, the outcome and the work mode travel as text because the
            // enums behind them are the Applications module's own. Widths match the
            // columns they were written from, so nothing can arrive too long for
            // the column it lands in.
            facts.Property(f => f.Stage).HasMaxLength(16);
            facts.Property(f => f.Outcome).HasMaxLength(16);
            facts.Property(f => f.WorkMode).HasMaxLength(16);
            facts.Property(f => f.Source).HasMaxLength(100);

            // No foreign keys at all - not to the application, the campaign, the
            // company or the owner. Every one of those ids belongs to another
            // module's schema, and a cross-schema foreign key is the boundary
            // violation this module is built on the far side of. The same
            // reasoning that made owner columns non-FK everywhere else applies
            // here to all four.
            //
            // One index, leading on the owner: every read is "this account's
            // rows", optionally narrowed to one campaign, and erasure takes the
            // same path. A grouped scan over the few hundred rows an account
            // reaches is not worth designing against - the read model aggregates
            // rather than counting precisely because that trade is available.
            facts.HasIndex(f => new { f.OwnerId, f.CampaignId });
        });
    }
}
