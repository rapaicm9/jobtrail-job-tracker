using Jobspect.Modules.Notifications.Domain;

namespace Jobspect.Modules.Notifications.Features.ArmReminders;

/// <summary>
/// One moment to arm, and what it is for. The pair <see cref="ReminderInstants"/>
/// hands back and the writer turns into a row.
/// <para>
/// The kind travels with the instant rather than being inferred from it, because
/// the slot a reminder occupies is the application, the round and the kind - two
/// instants computed for one interview are two different reminders, and a caller
/// that had to work out which was which from the timestamps would be re-deriving
/// something this type can simply state.
/// </para>
/// </summary>
internal readonly record struct ReminderInstant(ReminderKind Kind, DateTimeOffset DueAt);
