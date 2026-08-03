using Jobspect.SharedKernel;

namespace Jobspect.Modules.Notifications.Features;

internal static class ReminderErrors
{
    /// <summary>
    /// No such entry in this caller's feed.
    /// <para>
    /// It covers three different situations on purpose, and answers all of them the
    /// same way: the reminder does not exist, it belongs to somebody else, or it is a
    /// row that was never delivered - armed, retracted, or dropped as too late. The
    /// first two must be indistinguishable, or the 404 becomes a way to discover which
    /// ids exist. The third is not a evasion but a fact: the feed is what the owner was
    /// told, so a reminder outside it has no entry to address.
    /// </para>
    /// </summary>
    public static Error NotFound(Guid id) =>
        Error.NotFound("reminder.not_found", $"No reminder with id '{id}' was found.");
}
