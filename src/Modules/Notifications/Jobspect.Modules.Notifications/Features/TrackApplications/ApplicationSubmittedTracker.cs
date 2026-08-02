using Jobspect.Modules.Applications.Contracts;
using Jobspect.SharedKernel.Events;

namespace Jobspect.Modules.Notifications.Features.TrackApplications;

/// <summary>
/// A new application to watch for silence. This module starts remembering it from
/// here.
/// <para>
/// It arms nothing. The follow-up is raised by the scan, from a rule the account
/// may not have created yet - arming one on submission would leave a rule set up
/// afterwards with nothing to act on, which is the moment the automation is most
/// obviously wanted, by an account whose oldest applications are the silent ones.
/// </para>
/// </summary>
internal sealed class ApplicationSubmittedTracker(TrackedApplicationWriter writer)
    : IEventHandler<ApplicationSubmitted>
{
    public Task HandleAsync(ApplicationSubmitted integrationEvent, CancellationToken cancellationToken) =>
        writer.SubmittedAsync(
            integrationEvent.ApplicationId,
            integrationEvent.OwnerId,
            integrationEvent.AppliedDate,
            cancellationToken);
}
