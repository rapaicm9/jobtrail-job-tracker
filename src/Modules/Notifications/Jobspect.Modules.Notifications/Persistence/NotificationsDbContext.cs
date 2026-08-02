using Jobspect.Infrastructure.Persistence;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Notifications.Persistence;

/// <summary>
/// The Notifications module's private store, inside its own <c>notifications</c>
/// schema: the reminders it has armed, what it has delivered, the automation an
/// account configured, and the little it has to remember about applications in
/// order to notice one going unanswered.
/// <para>
/// There is no outbox here, and its absence is deliberate rather than an omission -
/// the modules that publish map one. This module consumes events and raises none,
/// so it owes nobody anything.
/// </para>
/// <para>
/// The scheduler's own tables land in this schema too, written by Quartz rather
/// than by these migrations. One module, one schema; a scheduler used by one module
/// is not an exception to it.
/// </para>
/// </summary>
internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<ReminderRule> ReminderRules => Set<ReminderRule>();

    public DbSet<ReminderDelivery> ReminderDeliveries => Set<ReminderDelivery>();

    public DbSet<TrackedApplication> TrackedApplications => Set<TrackedApplication>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        // Owner columns carry the strongly-typed id and store as uuid; one place,
        // so no property has to remember to opt in.
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        builder.Entity<Reminder>(reminder =>
        {
            reminder.HasKey(r => r.Id);
            reminder.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            reminder.Property(r => r.CreatedAt).HasDefaultValueSql("now()");

            // Both enums store as their names, like every other enum in this system.
            // The state is defaulted at the database as well, so an armed reminder
            // cannot be inserted without one and the sweep's predicate can never
            // miss a row for want of a value.
            reminder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(48).IsRequired();
            reminder.Property(r => r.State)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired()
                .HasDefaultValue(ReminderState.Pending);

            // The rule that raised a follow-up. Same-schema, so a real foreign key -
            // the cross-schema ban is about other modules, not within this one. It
            // nulls out rather than cascading: deleting the automation stops it
            // raising more, and does not rewrite what the owner was already told.
            reminder.HasOne<ReminderRule>()
                .WithMany()
                .HasForeignKey(r => r.RuleId)
                .OnDelete(DeleteBehavior.SetNull);

            // The sweep's whole access path: the oldest pending instants that have
            // come due. Partial, so the index stays the size of the outstanding work
            // rather than of everything ever sent.
            reminder.HasIndex(r => r.DueAt)
                .HasFilter("state = 'Pending'");

            // One armed instant per slot. Partial for the same reason it is the
            // right rule: a delivered reminder is history and must not stop the same
            // slot being armed again when the round moves.
            //
            // Nulls are non-distinct, and that is load-bearing rather than
            // decorative - interview_id is null on every kind except the two
            // interview ones, and under Postgres' default two such rows would both
            // be accepted, which is precisely the duplicate this index exists to
            // refuse.
            reminder.HasIndex(r => new { r.ApplicationId, r.InterviewId, r.Kind })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasFilter("state = 'Pending'");

            // Everything that reaches a reminder from the thing it is about starts
            // from the application: retracting what an answered application still
            // has pending, and reading the newest row for a slot to find out whether
            // an arriving event is older than what has already been decided. Covers
            // all states, unlike the index above.
            reminder.HasIndex(r => r.ApplicationId);

            // The feed, newest-first per owner with the id breaking ties, walked by
            // a cursor. Erasure takes the leading column.
            reminder.HasIndex(r => new { r.OwnerId, r.DueAt, r.Id });
        });

        builder.Entity<ReminderRule>(rule =>
        {
            rule.HasKey(r => r.Id);
            rule.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            rule.Property(r => r.CreatedAt).HasDefaultValueSql("now()");

            // One rule per account, held by the database rather than by a handler
            // that could forget to look. It doubles as the access path the scan
            // takes to find the accounts that have automated anything at all.
            rule.HasIndex(r => r.OwnerId).IsUnique();
        });

        builder.Entity<ReminderDelivery>(delivery =>
        {
            // The key is the guard: one delivery per reminder per channel, so a
            // second attempt at the same one is refused by the database instead of
            // reaching the owner twice. No surrogate id - there is nothing to
            // identify a delivery by other than what was delivered and how.
            delivery.HasKey(d => new { d.ReminderId, d.Channel });

            delivery.Property(d => d.Channel).HasConversion<string>().HasMaxLength(16);

            // No default on delivered_at: the sweep writes it from the clock it was
            // given, so a test can decide what "now" was.

            // A delivery says nothing without its reminder, so it goes when the
            // reminder goes.
            delivery.HasOne<Reminder>()
                .WithMany()
                .HasForeignKey(d => d.ReminderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrackedApplication>(tracked =>
        {
            // Keyed by the application the events are about, and without the
            // uuidv7() default the rows this module raises itself carry: the id
            // arrives on the event, and a rewritten key would insert a fresh row on
            // every redelivery instead of updating the one that is there.
            tracked.HasKey(t => t.ApplicationId);
            tracked.Property(t => t.ApplicationId).ValueGeneratedNever();

            tracked.Property(t => t.CreatedAt).HasDefaultValueSql("now()");

            // The stage travels as the text the event carried; the width matches the
            // column it was written from, so nothing can arrive too long for this one.
            tracked.Property(t => t.Stage).HasMaxLength(16);

            // No foreign key to the application, nor to the owner: both live in
            // another module's schema, which is the boundary this table exists on
            // the far side of.
            //
            // One index, leading on the owner: the scan runs per account that has a
            // rule, bounded by how long an application has been waiting, and erasure
            // takes the same path.
            tracked.HasIndex(t => new { t.OwnerId, t.AppliedDate });
        });
    }
}
