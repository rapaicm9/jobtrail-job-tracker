using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.CreateApplication;

/// <summary>
/// Opens a new application for the caller. It lands in the account's default
/// campaign (multiple campaigns is a Pro, later concern), references a company
/// resolved from the request, and starts at <c>Applied</c>. The application, any
/// newly-created company, and the first activity entry commit together, so the
/// timeline is never missing its opening row. When the client omits the applied
/// date it defaults to the caller's local today, computed from their stored
/// timezone - a date is only meaningful in a place.
/// </summary>
internal sealed class CreateApplicationHandler(
    ApplicationsDbContext dbContext,
    CompanyResolver companyResolver,
    CustomFieldValueResolver customFieldValues,
    IEntitlementQuery entitlements,
    IUserProfileQuery profileQuery,
    TimeProvider timeProvider)
{
    public async Task<Result<ApplicationResponse>> HandleAsync(
        UserId ownerId, CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var campaignId = await dbContext.Campaigns
            .Where(c => c.OwnerId == ownerId && c.IsDefault)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (campaignId is null)
        {
            return ApplicationErrors.NoDefaultCampaign;
        }

        var company = await companyResolver.ResolveAsync(
            ownerId, request.CompanyId, request.CompanyName, cancellationToken);
        if (company.IsFailure)
        {
            return company.Error;
        }

        // Custom-field answers are Pro. The gate cannot sit on the route - both
        // tiers open applications - so it sits here, on the one part of the
        // payload only one of them may write. Sending the bag at all is the write;
        // an empty one included, since clearing is a change like any other.
        var values = CustomFieldValues.Empty;
        if (request.CustomFields is { } requested)
        {
            if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.CustomFields, cancellationToken))
            {
                return CustomFieldErrors.ValuesNotEntitled;
            }

            var resolved = await customFieldValues.ResolveAsync(ownerId, requested, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            values = resolved.Value;
        }

        var application = new Application
        {
            // Generated here, not by the database, so the id is known before the
            // insert - the activity entry (and any new company) reference it in the
            // same SaveChanges.
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            CampaignId = campaignId.Value,
            CompanyId = company.Value,
            Role = request.Role!.Trim(),
            Compensation = ApplicationFieldMapping.ToMoney(request.Compensation),
            Location = ApplicationFieldMapping.Clean(request.Location),
            WorkMode = ApplicationFieldMapping.ParseWorkMode(request.WorkMode),
            PostingUrl = ApplicationFieldMapping.Clean(request.PostingUrl),
            Source = ApplicationFieldMapping.Clean(request.Source),
            AppliedDate = request.AppliedDate ?? await ResolveLocalTodayAsync(ownerId, cancellationToken),
            ApplicationDeadline = request.ApplicationDeadline,
            CvLabel = ApplicationFieldMapping.Clean(request.CvLabel),
            CoverLetterLabel = ApplicationFieldMapping.Clean(request.CoverLetterLabel),
            CustomFieldValues = values,
        };

        dbContext.Applications.Add(application);
        dbContext.ActivityLog.Add(ActivityLogEntry.Created(application.Id, ownerId));

        var now = timeProvider.GetUtcNow();

        // Announced in the same SaveChanges as the application itself, so the fact
        // and the announcement of it commit together. A consumer cannot read this
        // module's tables to catch up, so a lost event is a fact nobody else ever
        // learns - hence the outbox rather than in-memory dispatch.
        dbContext.Outbox.Add(OutboxMessage.For(
            new ApplicationSubmitted(
                Guid.CreateVersion7(),
                application.Id,
                ownerId,
                application.CampaignId,
                application.CompanyId,
                application.AppliedDate,
                application.Source,
                application.WorkMode?.ToString(),
                now)));

        // A deadline entered while opening the application is a deadline like any
        // other, and this is the event Notifications schedules from - the submission
        // does not carry one. Without this, a deadline typed in here would never
        // reach a reminder.
        if (application.ApplicationDeadline is not null)
        {
            dbContext.Outbox.Add(OutboxMessage.For(
                new ApplicationDeadlineSet(
                    Guid.CreateVersion7(), application.Id, ownerId, application.ApplicationDeadline, now)));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // CreatedAt and Stage are database-generated; EF reads them back onto the
        // entity after the insert, so the response is complete without a re-read.
        return application.ToResponse();
    }

    private async Task<DateOnly> ResolveLocalTodayAsync(UserId ownerId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var timezoneId = await profileQuery.GetTimezoneAsync(ownerId, cancellationToken);

        return timezoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var timezone)
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timezone).DateTime)
            : DateOnly.FromDateTime(nowUtc.UtcDateTime);
    }
}
