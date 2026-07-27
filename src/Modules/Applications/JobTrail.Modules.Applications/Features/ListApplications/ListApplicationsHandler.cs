using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features.ListApplications;

/// <summary>
/// Lists the caller's own applications, newest application first. The owner from
/// the token is the filter, so a user only ever sees their own. Ordering is by
/// applied date descending, then id (a UUIDv7, so time-ordered) to break ties -
/// two applications sent on the same day are common, and without the tiebreak a
/// page edge could repeat or skip one.
/// <para>
/// Paged by cursor rather than offset: the applied date and id of the last row
/// the client saw become "start after this", so an application added while the
/// client reads doesn't shift the rows underneath it.
/// </para>
/// <para>
/// A custom-field filter narrows it further, as a JSONB containment test the GIN
/// index over the column serves directly. That is Pro, and the check sits here
/// rather than on the route because both tiers read their applications - it is one
/// optional parameter that is out of reach, not the call.
/// </para>
/// </summary>
internal sealed class ListApplicationsHandler(
    ApplicationsDbContext dbContext,
    CustomFieldFilterResolver filterResolver,
    IEntitlementQuery entitlements)
{
    public async Task<Result<PagedResponse<ApplicationSummaryResponse>>> HandleAsync(
        UserId ownerId, CustomFieldFilter? filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = dbContext.Applications
            .AsNoTracking()
            .Where(a => a.OwnerId == ownerId);

        if (filter is not null)
        {
            if (!await entitlements.HasEntitlementAsync(ownerId, Entitlement.CustomFields, cancellationToken))
            {
                return CustomFieldErrors.QueryNotEntitled;
            }

            var probe = await filterResolver.ResolveAsync(ownerId, filter, cancellationToken);
            if (probe.IsFailure)
            {
                return probe.Error;
            }

            // Containment, not a path comparison: `@>` is the one operator a
            // jsonb_path_ops GIN index serves, and the probe was built to be the
            // JSON this field's values actually are.
            var contained = probe.Value;
            query = query.Where(a => EF.Functions.JsonContains(a.CustomFieldValues, contained));
        }

        if (page.Position is { } position && SortKeys.ToDate(position.SortKey) is { } appliedDate)
        {
            var lastId = position.Id;
            query = query.Where(a =>
                a.AppliedDate < appliedDate || (a.AppliedDate == appliedDate && a.Id < lastId));
        }

        // Rows map after materialization: the stored stage and work mode are
        // converted enums, so the string projection can't happen in SQL.
        return await PageBuilder.BuildAsync(
            query.OrderByDescending(a => a.AppliedDate).ThenByDescending(a => a.Id),
            page.Limit,
            a => a.ToSummary(),
            a => new Cursor(a.Id, SortKeys.From(a.AppliedDate)),
            cancellationToken);
    }
}
