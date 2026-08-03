using Jobspect.Modules.Notifications.Features.CountUnreadReminders;
using Jobspect.Modules.Notifications.Features.DismissReminder;
using Jobspect.Modules.Notifications.Features.ListReminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobspect.Modules.Notifications;

/// <summary>
/// What the API host serves out of this module: the feed a reminder finally reaches
/// somebody through.
/// <para>
/// <b>The fourth composition method, and called by the API host alone.</b> Each of the
/// four names who calls it - the store belongs to both hosts, the schedule to the
/// worker, the consumers and this to the API - and that is a property worth one extra
/// method rather than three registrations quietly added to the shared one. Nothing
/// here would break the worker if it were composed there, which is exactly why it
/// would go unnoticed: the worker has no HTTP surface, so it would carry three
/// services it can never reach and the sentence describing the composition would stop
/// being true.
/// </para>
/// <para>
/// These handlers need only this module's store. The caller is resolved at the edge,
/// so unlike the arming consumers they place no demand on Identity.
/// </para>
/// </summary>
public static class NotificationsFeed
{
    public static IHostApplicationBuilder AddNotificationsFeed(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<ListRemindersHandler>();
        builder.Services.AddScoped<CountUnreadRemindersHandler>();
        builder.Services.AddScoped<DismissReminderHandler>();

        return builder;
    }
}
