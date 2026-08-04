using Jobspect.Modules.Notifications.Features.ClearReminderRule;
using Jobspect.Modules.Notifications.Features.CountUnreadReminders;
using Jobspect.Modules.Notifications.Features.DismissReminder;
using Jobspect.Modules.Notifications.Features.GetReminderRule;
using Jobspect.Modules.Notifications.Features.ListReminders;
using Jobspect.Modules.Notifications.Features.SetReminderRule;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobspect.Modules.Notifications;

/// <summary>
/// What the API host serves out of this module: the feed a reminder finally reaches
/// somebody through, and the automation an account configures for itself.
/// <para>
/// <b>The fourth composition method, and called by the API host alone.</b> The four
/// split by how work reaches a handler rather than by feature - the store belongs to
/// both hosts, the schedule to the worker, the event consumers to the API because
/// that is where the dispatcher runs, and this to the API because everything in it is
/// entered through HTTP. Nothing here would break the worker if it were composed
/// there, which is exactly why it would go unnoticed: the worker serves no HTTP, so
/// it would carry handlers it can never reach and the sentence describing the
/// composition would stop being true.
/// </para>
/// <para>
/// These handlers need this module's store, the caller - resolved at the edge - and,
/// for the one gated write, the entitlement query. Unlike the arming consumers they
/// place no demand on Identity's profile read.
/// </para>
/// </summary>
public static class NotificationsApi
{
    public static IHostApplicationBuilder AddNotificationsApi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<ListRemindersHandler>();
        builder.Services.AddScoped<CountUnreadRemindersHandler>();
        builder.Services.AddScoped<DismissReminderHandler>();

        builder.Services.AddScoped<SetReminderRuleHandler>();
        builder.Services.AddScoped<GetReminderRuleHandler>();
        builder.Services.AddScoped<ClearReminderRuleHandler>();

        return builder;
    }
}
