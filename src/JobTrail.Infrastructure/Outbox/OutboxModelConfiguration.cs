using Microsoft.EntityFrameworkCore;

namespace JobTrail.Infrastructure.Outbox;

/// <summary>
/// The stored shape of an outbox, for the modules that keep one. The table lives
/// in the publishing module's schema - that is what lets a row share the
/// transaction of the change it announces - but the shape itself is the
/// dispatcher's, and the dispatcher reads every outbox the same way.
/// </summary>
public static class OutboxModelConfiguration
{
    /// <summary>
    /// Maps <see cref="OutboxMessage"/> into the calling context's default schema.
    /// Called from a module's <c>OnModelCreating</c>, after the default schema is
    /// set, so the table lands beside the rows it announces.
    /// </summary>
    public static ModelBuilder MapOutbox(this ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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

            // The second way these rows are read: by owner, when a user asks to be
            // forgotten and the events still owed on their behalf have to go with
            // the rest of their data. Rare, but it must not scan the backlog.
            message.HasIndex(m => m.OwnerId);
        });

        return builder;
    }
}
