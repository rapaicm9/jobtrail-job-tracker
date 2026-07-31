namespace JobTrail.Modules.Notifications.Domain;

/// <summary>
/// How a reminder reached its owner. One reminder can reach them more than one
/// way, which is why the channel sits on the delivery record rather than on the
/// reminder.
/// </summary>
internal enum DeliveryChannel
{
    /// <summary>
    /// The feed the client lists. The only channel in this release, and an insert
    /// rather than a call - there is nothing external to fail.
    /// </summary>
    InApp,
}
