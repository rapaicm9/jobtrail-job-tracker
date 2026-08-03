namespace Jobspect.Modules.Notifications.Features.SweepReminders;

/// <summary>
/// What one pass of the sweep did. Returned rather than only logged, because these
/// two numbers are the whole observable behaviour of a job that otherwise leaves
/// nothing to look at until the feed exists.
/// <para>
/// The two are deliberately separate rather than a single "handled" count. A
/// delivery and a drop are opposite outcomes - one reached the owner, the other was
/// owed and missed - and the metrics the hardening sprint adds count them apart for
/// the same reason.
/// </para>
/// </summary>
internal readonly record struct SweepOutcome(int Delivered, int Dropped);
