using System.Text.Json;
using Jobspect.Infrastructure.Outbox;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features.UpdateApplication;

/// <summary>
/// Replaces the editable fields of one of the caller's applications. Ownership is
/// the query, so another user's application is a 404. The pipeline stage is left
/// untouched - it moves only through the transition endpoint - and an offer-decision
/// deadline may only be <em>acquired or moved</em> while the application is at
/// <see cref="Stage.Offer"/>, since a decision deadline without an offer is
/// meaningless. One already recorded outlives the offer and round-trips unchanged.
/// Company follows the same reference-or-create resolution as create. A deadline
/// that moved is announced with the edit, since the modules that schedule reminders
/// from one cannot see the edit itself.
/// </summary>
internal sealed class UpdateApplicationHandler(
    ApplicationsDbContext dbContext,
    CompanyResolver companyResolver,
    CustomFieldValueResolver customFieldValues,
    OwnershipGuard ownership,
    IEntitlementQuery entitlements,
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

        // Compared against what is stored, so what this refuses is the change rather
        // than the value. An application only leaves Offer by closing and keeps the
        // deadline it was given, so the record a client reads back still carries it -
        // and a full replace sends back what it read. Judging the value alone would
        // make accepting an offer the moment the application stopped being editable.
        if (request.OfferDecisionDeadline is not null
            && request.OfferDecisionDeadline != application.OfferDecisionDeadline
            && application.Stage != Stage.Offer)
        {
            return ApplicationErrors.OfferDeadlineRequiresOffer;
        }

        var campaignId = request.CampaignId!.Value;
        if (!await ownership.OwnsCampaignAsync(ownerId, campaignId, cancellationToken))
        {
            return ApplicationErrors.UnknownCampaign(campaignId);
        }

        var company = await companyResolver.ResolveAsync(
            ownerId, request.CompanyId, request.CompanyName, cancellationToken);
        if (company.IsFailure)
        {
            return company.Error;
        }

        if (await ApplyCustomFieldsAsync(ownerId, application, request, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        // Where the deadlines and the campaign stood before the replace. An event is
        // owed only where one actually moved: a client re-sending an unchanged
        // record - which a full replace invites - would otherwise have Notifications
        // rescheduling the same reminder, and Analytics re-attributing the same
        // application, on every edit.
        var previousDeadline = application.ApplicationDeadline;
        var previousOfferDeadline = application.OfferDecisionDeadline;
        var previousCampaignId = application.CampaignId;

        application.CampaignId = campaignId;
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

        Announce(application, ownerId, previousDeadline, previousOfferDeadline, previousCampaignId, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return application.ToResponse();
    }

    /// <summary>
    /// Settles what this edit does to the custom-field answers, and returns the
    /// reason it may not, if there is one.
    /// <para>
    /// Entitlement decides whether the bag is writable at all, and the rest falls
    /// out of that. A caller who may write it gets the replace semantics every
    /// other field here has - send it and it is replaced, leave it off and it is
    /// cleared. A caller who may not write it is refused if they try, and their
    /// edit leaves the stored answers exactly as they were if they don't. That
    /// second case is the whole of "retained read-only": an account that has lost
    /// the entitlement can go on editing the rest of an application forever
    /// without its answers quietly draining away.
    /// </para>
    /// <para>
    /// The entitlement is read on every update, not only when the bag is present,
    /// because the absent case is exactly the one whose meaning depends on it.
    /// </para>
    /// </summary>
    private async Task<Error?> ApplyCustomFieldsAsync(
        UserId ownerId,
        Application application,
        UpdateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var mayWrite = await entitlements.HasEntitlementAsync(
            ownerId, Entitlement.CustomFields, cancellationToken);

        if (!mayWrite)
        {
            return request.CustomFields is null ? null : CustomFieldErrors.ValuesNotEntitled;
        }

        var resolved = await customFieldValues.ResolveAsync(
            ownerId, request.CustomFields ?? new Dictionary<Guid, JsonElement>(), cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        application.CustomFieldValues = resolved.Value;
        return null;
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
        Guid previousCampaignId,
        DateTimeOffset now)
    {
        // A move states both ends, so a consumer can apply it without having seen
        // the moves before it. Nothing schedules from this today; it is recorded
        // because a read model rebuilt from its event stream can never recover a
        // move that was never announced.
        if (application.CampaignId != previousCampaignId)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new ApplicationMovedToCampaign(
                    Guid.CreateVersion7(),
                    application.Id,
                    ownerId,
                    previousCampaignId,
                    application.CampaignId,
                    now)));
        }

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
