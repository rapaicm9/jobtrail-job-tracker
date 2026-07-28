using System.Text.Json;
using System.Text.Json.Nodes;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ExportData;

/// <summary>
/// The Applications module's contribution to an account export, and by far the
/// largest: the whole record of a job search, including the parts the user wrote
/// themselves.
/// <para>
/// Flat sections keyed by id rather than applications with their children nested
/// inside them. Nesting reads more nicely right up to the contact linked only to a
/// company, which has no application to sit under - so a nested document would need
/// a second home for that one case, and one entity written two ways is worse than
/// a shape that reads a little flatter. This also stays trivially convertible to a
/// table per section, which is what an export is eventually asked for.
/// </para>
/// <para>
/// Every read is scoped to the owner, and the outbox is not among them: what this
/// module still owes other modules is its own delivery bookkeeping, not anything
/// the user put in.
/// </para>
/// </summary>
internal sealed class ApplicationsDataExporter(ApplicationsDbContext dbContext) : IUserDataExporter
{
    public string Section => "applications";

    public async Task<JsonNode> ExportAsync(UserId userId, CancellationToken cancellationToken)
    {
        var campaigns = await dbContext.Campaigns
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new CampaignExport(c.Id, c.Name, c.IsDefault, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        var companies = await dbContext.Companies
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CompanyExport(c.Id, c.Name, c.Website, c.Notes, c.CreatedAt))
            .ToListAsync(cancellationToken);

        var customFields = await dbContext.CustomFields
            .AsNoTracking()
            .Where(d => d.OwnerId == userId)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
            .Select(d => new CustomFieldExport(
                d.Id, d.Label, d.Type.ToString(), d.Options, d.IsArchived, d.CreatedAt, d.UpdatedAt))
            .ToListAsync(cancellationToken);

        // Materialized before projecting: the stage, the work mode and the answer
        // bag are all converted properties, so the provider cannot translate them
        // into the shape they are exported as.
        var applicationRows = await dbContext.Applications
            .AsNoTracking()
            .Where(a => a.OwnerId == userId)
            .OrderBy(a => a.AppliedDate).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var applications = applicationRows
            .Select(a => new ApplicationExport(
                a.Id,
                a.CampaignId,
                a.CompanyId,
                a.Stage.ToString(),
                a.Role,
                a.Compensation?.Amount,
                a.Compensation?.Currency,
                a.Location,
                a.WorkMode?.ToString(),
                a.PostingUrl,
                a.Source,
                a.AppliedDate,
                a.ApplicationDeadline,
                a.OfferDecisionDeadline,
                a.CvLabel,
                a.CoverLetterLabel,
                a.CustomFieldValues.Values,
                a.CreatedAt,
                a.UpdatedAt))
            .ToList();

        var contacts = await dbContext.Contacts
            .AsNoTracking()
            .Where(c => c.OwnerId == userId)
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new ContactExport(
                c.Id, c.ApplicationId, c.CompanyId, c.Name, c.Role.ToString(),
                c.Email, c.Phone, c.Notes, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        var interviewRows = await dbContext.Interviews
            .AsNoTracking()
            .Where(i => i.OwnerId == userId)
            .OrderBy(i => i.ScheduledAt).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var interviews = interviewRows
            .Select(i => new InterviewExport(
                i.Id, i.ApplicationId, i.ScheduledAt, i.Type.ToString(), i.Format.ToString(),
                i.Outcome.ToString(), i.Notes, i.CreatedAt, i.UpdatedAt))
            .ToList();

        var activityRows = await dbContext.ActivityLog
            .AsNoTracking()
            .Where(e => e.OwnerId == userId)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        var activity = activityRows
            .Select(e => new ActivityExport(
                e.Id, e.ApplicationId, e.Kind.ToString(), e.FromStage?.ToString(), e.ToStage?.ToString(),
                e.TransitionKind?.ToString(), e.Note, e.CreatedAt))
            .ToList();

        var export = new ApplicationsExport(
            campaigns, companies, customFields, applications, contacts, interviews, activity);

        return JsonSerializer.SerializeToNode(export, ExportJson.Options) ?? new JsonObject();
    }

    private sealed record ApplicationsExport(
        IReadOnlyList<CampaignExport> Campaigns,
        IReadOnlyList<CompanyExport> Companies,
        IReadOnlyList<CustomFieldExport> CustomFields,
        IReadOnlyList<ApplicationExport> Applications,
        IReadOnlyList<ContactExport> Contacts,
        IReadOnlyList<InterviewExport> Interviews,
        IReadOnlyList<ActivityExport> Activity);

    private sealed record CampaignExport(
        Guid Id, string Name, bool IsDefault, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    private sealed record CompanyExport(
        Guid Id, string Name, string? Website, string? Notes, DateTimeOffset CreatedAt);

    private sealed record CustomFieldExport(
        Guid Id,
        string Label,
        string Type,
        IReadOnlyList<string> Options,
        bool IsArchived,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record ApplicationExport(
        Guid Id,
        Guid CampaignId,
        Guid? CompanyId,
        string Stage,
        string Role,
        decimal? CompensationAmount,
        string? CompensationCurrency,
        string? Location,
        string? WorkMode,
        string? PostingUrl,
        string? Source,
        DateOnly AppliedDate,
        DateOnly? ApplicationDeadline,
        DateOnly? OfferDecisionDeadline,
        string? CvLabel,
        string? CoverLetterLabel,
        IReadOnlyDictionary<Guid, JsonElement> CustomFields,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record ContactExport(
        Guid Id,
        Guid? ApplicationId,
        Guid? CompanyId,
        string Name,
        string? Role,
        string? Email,
        string? Phone,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record InterviewExport(
        Guid Id,
        Guid ApplicationId,
        DateTimeOffset ScheduledAt,
        string Type,
        string Format,
        string Outcome,
        string? Notes,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record ActivityExport(
        Guid Id,
        Guid ApplicationId,
        string Kind,
        string? FromStage,
        string? ToStage,
        string? TransitionKind,
        string? Note,
        DateTimeOffset CreatedAt);
}
