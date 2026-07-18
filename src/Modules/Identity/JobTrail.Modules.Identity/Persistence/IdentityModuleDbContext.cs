using JobTrail.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Identity.Persistence;

/// <summary>
/// The Identity module's private store. Uses the role-free Identity context
/// (authorization is policy- and entitlement-based, not role-based) and keeps
/// every table inside the module's own <c>identity</c> schema.
/// </summary>
internal sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        // Identity ships PascalCase table names ("AspNetUsers"); the snake_case
        // convention renames columns and constraints but leaves these explicit
        // names, so rename them here to keep the whole schema snake_case.
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.TimeZoneId).HasMaxLength(64).IsRequired();
            user.Property(u => u.TokenVersion).HasDefaultValue(0);
            user.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        });

        builder.Entity<RefreshToken>(token =>
        {
            token.HasKey(t => t.Id);
            token.Property(t => t.Id).HasDefaultValueSql("uuidv7()");
            token.Property(t => t.TokenHash).IsRequired();
            token.Property(t => t.DeviceLabel).HasMaxLength(128);
            token.Property(t => t.CreatedAt).HasDefaultValueSql("now()");

            // Look-ups: by token (verifying a presented refresh token) and by
            // family (revoking everything descended from one login).
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.FamilyId);

            token.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
