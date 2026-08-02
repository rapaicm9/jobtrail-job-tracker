namespace Jobspect.Modules.Notifications.Domain;

/// <summary>
/// A reminder actually reached its owner, this way, at this moment.
/// <para>
/// The division of labour with <see cref="Reminder.State"/> is worth stating,
/// because at one channel the two look like the same fact written twice. The state
/// is the <em>decision</em> - this reminder has been dealt with, stop sweeping it.
/// This is the <em>act</em>, and it holds the one thing the reminder row cannot:
/// when the owner was told, which is not when the reminder was due. The sweep
/// discovers everything slightly late, and a reminder found too late is never
/// delivered at all.
/// </para>
/// <para>
/// Its key is what makes at-least-once safe. Two sweeps that both claim the same
/// reminder produce the same key, and the second insert is refused rather than
/// delivering twice. That guarantee belongs to the pipeline rather than to any one
/// channel, which is why it exists before there is a second channel to need it.
/// </para>
/// </summary>
internal sealed class ReminderDelivery
{
    /// <summary>
    /// The reminder that was delivered. Half the key, and a foreign key that
    /// cascades: a delivery says nothing on its own, so it goes when the reminder
    /// goes.
    /// </summary>
    public Guid ReminderId { get; set; }

    /// <summary>
    /// How it was delivered, and the other half of the key - one delivery per
    /// reminder per channel, which is what a repeat has to violate to be a no-op.
    /// </summary>
    public DeliveryChannel Channel { get; set; }

    /// <summary>
    /// When the owner was told. Written by the sweep from the clock it was given,
    /// deliberately not defaulted at the database: a delivery time nothing can
    /// control is a delivery time nothing can test.
    /// </summary>
    public DateTimeOffset DeliveredAt { get; set; }
}
