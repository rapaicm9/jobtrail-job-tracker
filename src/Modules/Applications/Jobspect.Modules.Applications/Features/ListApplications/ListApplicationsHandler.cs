using System.Text;
using Jobspect.Infrastructure.Persistence;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobspect.Modules.Applications.Features.ListApplications;

/// <summary>
/// Lists the caller's own applications. The owner from the token is always the
/// filter, so a user only ever sees their own.
/// <para>
/// By default they come newest-applied first, tie-broken by id (a UUIDv7, so
/// time-ordered) - two applications sent on the same day are common, and without
/// the tiebreak a page edge could repeat or skip one. A custom-field sort replaces
/// that ordering; a custom-field filter narrows either, and so does a campaign.
/// </para>
/// <para>
/// The custom-field parameters are Pro, and the check sits here rather than on the
/// route because both tiers read their applications - it is those parameters that
/// are out of reach, not the call. Narrowing to a campaign is not gated: a Free
/// account has one campaign, and a downgraded account still has to be able to look
/// inside the ones it holds.
/// </para>
/// </summary>
internal sealed class ListApplicationsHandler(
    ApplicationsDbContext dbContext,
    CustomFieldFilterResolver filterResolver,
    CustomFieldSortResolver sortResolver,
    OwnershipGuard ownership,
    IEntitlementQuery entitlements)
{
    public async Task<Result<PagedResponse<ApplicationSummaryResponse>>> HandleAsync(
        UserId ownerId,
        Guid? campaignId,
        CustomFieldFilter? filter,
        CustomFieldSort? sort,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        if ((filter is not null || sort is not null)
            && !await entitlements.HasEntitlementAsync(ownerId, Entitlement.CustomFields, cancellationToken))
        {
            return CustomFieldErrors.QueryNotEntitled;
        }

        // A campaign that is not the caller's own is refused rather than answered
        // with an empty page - an empty page already means "nothing matched", and a
        // client cannot tell a mistyped id from a campaign it has emptied.
        if (campaignId is { } id && !await ownership.OwnsCampaignAsync(ownerId, id, cancellationToken))
        {
            return ApplicationErrors.UnknownCampaign(id);
        }

        string? probe = null;
        if (filter is not null)
        {
            var resolved = await filterResolver.ResolveAsync(ownerId, filter, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            probe = resolved.Value;
        }

        if (sort is null)
        {
            return await ByAppliedDateAsync(ownerId, campaignId, probe, page, cancellationToken);
        }

        var plan = await sortResolver.ResolveAsync(ownerId, sort, cancellationToken);
        return plan.IsFailure
            ? plan.Error
            : await ByAnswerAsync(ownerId, campaignId, probe, plan.Value, page, cancellationToken);
    }

    /// <summary>The default order, which LINQ expresses and the composite index serves.</summary>
    private Task<PagedResponse<ApplicationSummaryResponse>> ByAppliedDateAsync(
        UserId ownerId, Guid? campaignId, string? probe, PageRequest page, CancellationToken cancellationToken)
    {
        var query = dbContext.Applications
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId);

        if (campaignId is { } id)
        {
            query = query.Where(a => a.CampaignId == id);
        }

        if (probe is not null)
        {
            // Containment, not a path comparison: `@>` is the one operator a
            // jsonb_path_ops GIN index serves, and the probe was built to be the
            // JSON this field's values actually are.
            query = query.Where(a => EF.Functions.JsonContains(a.CustomFieldValues, probe));
        }

        if (page.Position is { } position && SortKeys.ToDate(position.SortKey) is { } appliedDate)
        {
            var lastId = position.Id;
            query = query.Where(a =>
                a.AppliedDate < appliedDate || (a.AppliedDate == appliedDate && a.Id < lastId));
        }

        // Rows map after materialization: the stored stage and work mode are
        // converted enums, so the string projection can't happen in SQL.
        return PageBuilder.BuildAsync(
            query.OrderByDescending(a => a.AppliedDate).ThenByDescending(a => a.Id),
            page.Limit,
            a => a.ToSummary(),
            a => new Cursor(a.Id, SortKeys.From(a.AppliedDate)),
            cancellationToken);
    }

    /// <summary>
    /// Ordered by one custom-field answer, which has to be SQL rather than LINQ:
    /// the ordering is <c>custom_field_values -&gt;&gt; $field</c>, and no
    /// expression tree produces that. Composing an <c>ORDER BY</c> outside a
    /// <c>FromSql</c> would be the only thing that decided row order, so the whole
    /// query - predicate, order, limit - is written here and nothing is layered on
    /// top of it.
    /// <para>
    /// Everything a client sent travels as a parameter, the field id included: it
    /// is the right-hand side of an operator, not a fragment of SQL. The only
    /// pieces built into the text are chosen from the plan - a direction keyword
    /// and whether the column is cast to numeric.
    /// </para>
    /// <para>
    /// Unanswered applications sort last whichever way the answers run, so the
    /// resume condition has three cases rather than two: past this answer, level
    /// with it and past this id, or anywhere in the unanswered tail. Get that
    /// wrong and a page boundary quietly repeats or drops the rows around it.
    /// </para>
    /// </summary>
    private async Task<Result<PagedResponse<ApplicationSummaryResponse>>> ByAnswerAsync(
        UserId ownerId,
        Guid? campaignId,
        string? probe,
        CustomFieldSortPlan plan,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var order = plan.OrderExpression("@fieldId");
        var direction = plan.Descending ? "DESC" : "ASC";
        var past = plan.Descending ? "<" : ">";

        var parameters = new List<NpgsqlParameter>
        {
            new("owner", ownerId.Value),
            new("fieldId", plan.FieldId.ToString()),

            // One more than asked for, so the envelope can tell whether anything
            // follows without counting what follows.
            new("limit", page.Limit + 1),
        };

        var where = new StringBuilder("a.owner_id = @owner");

        // Every narrowing the LINQ path applies has to be applied here too: this
        // query is written whole, so a filter omitted from it is a filter the client
        // asked for and silently did not get.
        if (campaignId is { } id)
        {
            parameters.Add(new NpgsqlParameter("campaignId", id));
            where.Append(" AND a.campaign_id = @campaignId");
        }

        if (probe is not null)
        {
            parameters.Add(new NpgsqlParameter("probe", probe));
            where.Append(" AND a.custom_field_values @> @probe::jsonb");
        }

        if (page.Position is { } position && SortKeys.ToAnswer(position.SortKey) is { } resume)
        {
            parameters.Add(new NpgsqlParameter("lastId", position.Id));

            if (!resume.Answered)
            {
                where.Append($" AND {order} IS NULL AND a.id {past} @lastId");
            }
            else if (plan.ToParameter(resume.Answer) is { } last)
            {
                parameters.Add(new NpgsqlParameter("last", last));
                where.Append(
                    $" AND (({order} IS NOT NULL AND {order} {past} @last)"
                    + $" OR ({order} = @last AND a.id {past} @lastId)"
                    + $" OR {order} IS NULL)");
            }
            else
            {
                // The cursor decoded and is answer-shaped, but its answer is not
                // this field's kind - it came from a sort on a different field.
                // Refused rather than restarted: a client looping over page one
                // forever is worse than an error.
                return CustomFieldErrors.SortCursorMismatch;
            }
        }

        var sql = $"""
            SELECT a.* FROM {ApplicationsDbContext.Schema}.applications AS a
            WHERE {where}
            ORDER BY {order} {direction} NULLS LAST, a.id {direction}
            LIMIT @limit
            """;

        var rows = await dbContext.Applications
            .FromSqlRaw(sql, [.. parameters])
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return PageBuilder.FromRows(
            rows,
            page.Limit,
            a => a.ToSummary(),
            a => new Cursor(a.Id, SortKeys.ForAnswer(plan.Render(a.CustomFieldValues))));
    }
}
