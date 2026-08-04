using Jobspect.Modules.Notifications.Features.ClearReminderRule;
using Jobspect.Modules.Notifications.Features.CountUnreadReminders;
using Jobspect.Modules.Notifications.Features.DismissReminder;
using Jobspect.Modules.Notifications.Features.GetReminderRule;
using Jobspect.Modules.Notifications.Features.ListReminders;
using Jobspect.Modules.Notifications.Features.SetReminderRule;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Notifications;

/// <summary>
/// This module's HTTP surface: the reminder feed, and the rule that raises one kind
/// of them.
/// <para>
/// There is no route for a single reminder. A feed entry is not a resource a client
/// navigates to - it is read in a page and cleared in place - which is why nothing
/// here returns a <c>Location</c> either, the same shape the activity timeline
/// settled on.
/// </para>
/// <para>
/// The rule is not a collection either, and takes no id: an account has one or has
/// none. Its three routes are mapped straight onto the versioned group rather than
/// through a group of their own, which is what keeps them addressed as
/// <c>/reminder-rule</c> - an endpoint mapped with an empty pattern on a group is
/// reached without a trailing slash but described with one.
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

        GetReminderRuleEndpoint.Map(api);
        SetReminderRuleEndpoint.Map(api);
        ClearReminderRuleEndpoint.Map(api);

        return reminders;
    }
}
