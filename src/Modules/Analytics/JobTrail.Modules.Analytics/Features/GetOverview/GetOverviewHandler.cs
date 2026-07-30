using JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;
using JobTrail.Modules.Analytics.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Analytics.Features.GetOverview;

/// <summary>
/// Counts the caller's base rows, grouped by the stage they are sitting at.
/// <para>
/// One grouped scan answers both figures - the total is the sum of the groups -
/// which is the whole reason the two arrive together rather than as separate
/// endpoints. Aggregating on read costs more than reading a counter and is what
/// ADR 0016 bought the absence of counters with: at the few hundred rows one job
/// search reaches, the scan is not a cost worth designing against.
/// </para>
/// <para>
/// No <c>Result</c>, because there is no failure to report. An account with
/// nothing recorded has an empty dashboard, not a missing one.
/// </para>
/// </summary>
internal sealed class GetOverviewHandler(AnalyticsDbContext dbContext)
{
    public async Task<AnalyticsOverviewResponse> HandleAsync(
        UserId ownerId, Guid? campaignId, CancellationToken cancellationToken)
    {
        var rows = dbContext.ApplicationFacts
            .AsNoTracking()
            .Where(facts => facts.OwnerId == ownerId);

        // Narrowed to one campaign when asked. Ungated: reading is never gated, and
        // an account with a single campaign simply gets the same numbers back. A
        // campaign that is not the caller's matches nothing and yields zeros, which
        // is both the right answer and one that tells them nothing about it.
        if (campaignId is { } campaign)
        {
            rows = rows.Where(facts => facts.CampaignId == campaign);
        }

        var groups = await rows
            .GroupBy(facts => facts.Stage)
            .Select(group => new { Stage = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // A row can exist before any stage is known: an interview or a campaign
        // move creates one if it arrives first, and neither carries a stage. It is
        // still an application the account recorded, so it counts toward the total
        // - but null is not a stage, so it is not a column in the snapshot.
        var total = groups.Sum(group => group.Count);

        var pipeline = groups
            .Where(group => group.Stage is not null)
            .OrderBy(group => PipelineStages.Order(group.Stage!))
            .ThenBy(group => group.Stage, StringComparer.Ordinal)
            .Select(group => new PipelineStageCount(group.Stage!, group.Count))
            .ToArray();

        return new AnalyticsOverviewResponse(total, pipeline);
    }
}
