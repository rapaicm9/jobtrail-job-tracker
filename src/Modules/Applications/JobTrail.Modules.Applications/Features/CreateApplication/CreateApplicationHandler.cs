using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
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
        };

        dbContext.Applications.Add(application);
        dbContext.ActivityLog.Add(ActivityLogEntry.Created(application.Id, ownerId));
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
