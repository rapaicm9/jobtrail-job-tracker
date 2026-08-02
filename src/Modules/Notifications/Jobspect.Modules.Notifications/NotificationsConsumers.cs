using Jobspect.Infrastructure.Events;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.Modules.Notifications.Features.EraseData;
using Jobspect.Modules.Notifications.Features.TrackApplications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Jobspect.Modules.Notifications;

/// <summary>
/// What this module does with the events it consumes: arm reminders, keep the
/// record the follow-up scan reads, and give it all back on erasure.
/// <para>
/// <b>The third composition method, and called by the API host alone.</b> The
/// module already separates the store (both hosts) from the schedule (the worker
/// only); this completes the split at the other end. Reminders are armed where the
/// outbox dispatcher runs and the events arrive, which is the API - the worker
/// composes no dispatcher and would never fire these handlers.
/// </para>
/// <para>
/// It is a separate method rather than a few more lines in
/// <see cref="NotificationsModule.AddNotificationsModule"/> for a concrete reason,
/// not symmetry. These handlers need <see cref="IUserProfileQuery"/>, the worker
/// does not compose Identity, and container validation is on by default in
/// Development - so registering them in the shared method would stop the worker
/// starting, on a dependency it has no reason to have and would never resolve.
/// </para>
/// </summary>
public static class NotificationsConsumers
{
    public static IHostApplicationBuilder AddNotificationsConsumers(this IHostApplicationBuilder builder)
    {
        // The SQL these handlers write through. Scoped, like the context they hold.
        builder.Services.AddScoped<ReminderWriter>();
        builder.Services.AddScoped<TrackedApplicationWriter>();

        // The date-bearing events. Each arms the instants still ahead of us and
        // retracts the kinds that no longer have one, so a reschedule, a deadline
        // moved closer than its own lead time, and a deadline cleared to null all
        // travel the same path.
        builder.Services.AddEventHandler<InterviewScheduled, InterviewScheduledHandler>();
        builder.Services.AddEventHandler<ApplicationDeadlineSet, ApplicationDeadlineSetHandler>();
        builder.Services.AddEventHandler<OfferDecisionDeadlineSet, OfferDecisionDeadlineSetHandler>();

        // The little this module has to remember to notice an application going
        // unanswered. It cannot ask the module that owns them, and the scan that
        // reads this runs in a process that does not host it.
        builder.Services.AddEventHandler<ApplicationSubmitted, ApplicationSubmittedTracker>();
        builder.Services.AddEventHandler<ApplicationStageChanged, ApplicationStageChangedTracker>();

        // And gives it all back on the way out: this module's share of the erasure
        // fan-out, from its own schema only. Registered last here, and after the
        // Applications module in the host - see the handler for why the order is
        // load-bearing.
        builder.Services.AddEventHandler<UserDataDeletionRequested, NotificationsDataErasureHandler>();

        // The clock the past-instant rule is judged against. Registered defensively:
        // the module should stand up whether or not another one got here first.
        builder.Services.TryAddSingleton(TimeProvider.System);

        return builder;
    }
}
