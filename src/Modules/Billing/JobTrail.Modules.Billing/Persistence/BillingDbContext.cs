using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Billing.Domain;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Billing.Persistence;

/// <summary>
/// The Billing module's private store: the per-user plan that is the entitlement
/// truth, and the purchases that produced it, both inside the module's own
/// <c>billing</c> schema.
/// </summary>
internal sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        // Owner columns carry the strongly-typed id and store as uuid; one place,
        // so no property has to remember to opt in.
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        builder.Entity<Plan>(plan =>
        {
            plan.HasKey(p => p.Id);
            plan.Property(p => p.Id).HasDefaultValueSql("uuidv7()");
            plan.Property(p => p.Tier).HasConversion<string>().HasMaxLength(16).IsRequired();
            plan.Property(p => p.CreatedAt).HasDefaultValueSql("now()");

            // One plan per account - the database is the arbiter, so the create
            // path can lean on the violation instead of a racy pre-check.
            plan.HasIndex(p => p.UserId).IsUnique();
        });

        builder.Entity<Purchase>(purchase =>
        {
            purchase.HasKey(p => p.Id);
            purchase.Property(p => p.Id).HasDefaultValueSql("uuidv7()");
            purchase.Property(p => p.ProviderReference).HasMaxLength(200).IsRequired();
            purchase.Property(p => p.CreatedAt).HasDefaultValueSql("now()");

            // A user's purchase history, read back by owner.
            purchase.HasIndex(p => p.UserId);
        });
    }
}
