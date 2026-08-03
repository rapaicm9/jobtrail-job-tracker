using Jobspect.Modules.Notifications.Features.CountUnreadReminders;
using Jobspect.Modules.Notifications.Features.DismissReminder;
using Jobspect.Modules.Notifications.Features.ListReminders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications;

/// <summary>
/// This module's HTTP surface: the reminder feed, and in this release nothing else.
/// <para>
/// There is no route for a single reminder. A feed entry is not a resource a client
/// navigates to - it is read in a page and cleared in place - which is why nothing
/// here returns a <c>Location</c> either, the same shape the activity timeline
/// settled on.
/// </para>
/// </summary>
public static class NotificationsEndpoints
{
    public static RouteGroupBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var reminders = api.MapGroup("/reminders");

        ListRemindersEndpoint.Map(reminders);
        CountUnreadRemindersEndpoint.Map(reminders);
        DismissReminderEndpoint.Map(reminders);

        return reminders;
    }
}
