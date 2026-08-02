namespace Jobspect.Modules.Notifications.Domain;

/// <summary>
/// Where a reminder is in its life. There is no scheduler entry beside it - the
/// row is the whole record of the reminder - so this column is what the sweep
/// reads to decide whether there is anything left to do.
/// </summary>
internal enum ReminderState
{
    /// <summary>Armed and waiting for its instant. The only state the sweep looks at.</summary>
    Pending,

    /// <summary>Delivered, and sitting unread in the owner's feed.</summary>
    Sent,

    /// <summary>Delivered and since dismissed by the owner. Stays in the feed; stops counting as unread.</summary>
    Dismissed,

    /// <summary>
    /// Retracted before it fired - the interview was cancelled, the deadline
    /// cleared, the application answered or closed, or the round moved and this
    /// instant retired in favour of a later one.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Found by the sweep more than the tolerated lateness past its instant and
    /// deliberately not sent. Distinct from <see cref="Cancelled"/> on purpose:
    /// nobody retracted this one, it was owed and missed, and the two answer
    /// different questions when the reminder feed is quiet.
    /// </summary>
    Dropped,
}
