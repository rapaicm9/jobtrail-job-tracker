using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// Replaces the editable fields of one of the caller's applications. Ownership is
/// the query, so another user's application is a 404. The pipeline stage is left
/// untouched - it moves only through the transition endpoint - and the
/// offer-decision deadline is refused unless the application is at
/// <see cref="Stage.Offer"/>, since a decision deadline without an offer is
/// meaningless. Company follows the same reference-or-create resolution as create.
/// A deadline that moved is announced with the edit, since the modules that
/// schedule reminders from one cannot see the edit itself.
/// </summary>
internal sealed class UpdateApplicationHandler(
    ApplicationsDbContext dbContext,
    CompanyResolver companyResolver,
    TimeProvider timeProvider)
{
    public async Task<Result<ApplicationResponse>> HandleAsync(
        UserId ownerId, Guid id, UpdateApplicationRequest request, CancellationToken cancellationToken)
    {
        var application = await dbContext.Applications
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(id);
        }

        if (request.OfferDecisionDeadline is not null && application.Stage != Stage.Offer)
        {
            return ApplicationErrors.OfferDeadlineRequiresOffer;
        }

        var company = await companyResolver.ResolveAsync(
            ownerId, request.CompanyId, request.CompanyName, cancellationToken);
        if (company.IsFailure)
        {
            return company.Error;
        }

        // Where the deadlines stood before the replace. An event is owed only where
        // one actually moved: a client re-sending an unchanged record - which a full
        // replace invites - would otherwise have Notifications rescheduling the same
        // reminder on every edit.
        var previousDeadline = application.ApplicationDeadline;
        var previousOfferDeadline = application.OfferDecisionDeadline;

        application.CompanyId = company.Value;
        application.Role = request.Role!.Trim();
        application.Compensation = ApplicationFieldMapping.ToMoney(request.Compensation);
        application.Location = ApplicationFieldMapping.Clean(request.Location);
        application.WorkMode = ApplicationFieldMapping.ParseWorkMode(request.WorkMode);
        application.PostingUrl = ApplicationFieldMapping.Clean(request.PostingUrl);
        application.Source = ApplicationFieldMapping.Clean(request.Source);
        application.AppliedDate = request.AppliedDate!.Value;
        application.ApplicationDeadline = request.ApplicationDeadline;
        application.OfferDecisionDeadline = request.OfferDecisionDeadline;
        application.CvLabel = ApplicationFieldMapping.Clean(request.CvLabel);
        application.CoverLetterLabel = ApplicationFieldMapping.Clean(request.CoverLetterLabel);

        var now = timeProvider.GetUtcNow();
        application.UpdatedAt = now;

        Announce(application, ownerId, previousDeadline, previousOfferDeadline, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return application.ToResponse();
    }

    /// <summary>
    /// Records the deadline changes other modules are owed, in the same
    /// SaveChanges as the edit. A cleared deadline is announced as loudly as a new
    /// one - the null says the date is gone, and a consumer holding a reminder for
    /// it has no other way to learn that it should drop it.
    /// </summary>
    private void Announce(
        Application application,
        UserId ownerId,
        DateOnly? previousDeadline,
        DateOnly? previousOfferDeadline,
        DateTimeOffset now)
    {
        if (application.ApplicationDeadline != previousDeadline)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new ApplicationDeadlineSet(
                    Guid.CreateVersion7(), application.Id, ownerId, application.ApplicationDeadline, now)));
        }

        if (application.OfferDecisionDeadline != previousOfferDeadline)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new OfferDecisionDeadlineSet(
                    Guid.CreateVersion7(), application.Id, ownerId, application.OfferDecisionDeadline, now)));
        }
    }
}
