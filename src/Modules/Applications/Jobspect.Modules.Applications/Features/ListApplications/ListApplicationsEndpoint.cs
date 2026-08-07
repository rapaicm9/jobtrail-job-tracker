using Jobspect.Modules.Applications.Features;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Jobspect.Modules.Applications.Features.ListApplications;

/// <summary>
/// <c>GET /applications?campaignId=&amp;customFieldId=&amp;customFieldValue=</c> - one
/// page of the caller's own applications as list rows, newest first, optionally
/// narrowed to one campaign and to those answering one of the account's custom
/// fields with a given value. Scoped to the token's subject; a user never sees
/// another's. Takes <c>limit</c> and the <c>cursor</c> a previous page returned.
/// <para>
/// The two filter parameters travel together: an id says which field, a value says
/// what to match, and one without the other is a request that cannot be answered.
/// Whether the value suits the field's type needs the definition, so the handler
/// settles that.
/// </para>
/// </summary>
internal static class ListApplicationsEndpoint
{
    public static void Map(IEndpointRouteBuilder applications) =>
        applications.MapGet("", HandleAsync)
            .WithName("listApplications")
            .RequireAuthorization();

    private static async Task<Results<Ok<PagedResponse<ApplicationSummaryResponse>>, ProblemHttpResult>> HandleAsync(
        Guid? campaignId,
        Guid? customFieldId,
        string? customFieldValue,
        Guid? sortCustomFieldId,
        string? sortDirection,
        int? limit,
        string? cursor,
        IUserContext userContext,
        ListApplicationsHandler handler,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } ownerId)
        {
            return Caller.MissingSubject.ToProblem();
        }

        if (ValidateQuery(customFieldId, customFieldValue, sortCustomFieldId, sortDirection) is { } queryErrors)
        {
            return Problems.Validation(queryErrors);
        }

        // Which cursors this list accepts depends on how it is ordered, so the
        // check has to know: a position in a custom-field sort means nothing to the
        // default order, and the other way round.
        var sortKey = sortCustomFieldId is null ? SortKeyKind.Date : SortKeyKind.Answer;
        if (PagingParameters.Validate(limit, cursor, sortKey) is { } errors)
        {
            return Problems.Validation(errors);
        }

        var filter = customFieldId is { } fieldId ? new CustomFieldFilter(fieldId, customFieldValue!) : null;
        var sort = sortCustomFieldId is { } sortFieldId
            ? new CustomFieldSort(sortFieldId, Descending(sortDirection))
            : null;

        var result = await handler.HandleAsync(
            ownerId, campaignId, filter, sort, PagingParameters.From(limit, cursor), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }

    /// <summary>Descending unless the client asked otherwise; the default list reads newest-first too.</summary>
    private static bool Descending(string? sortDirection) =>
        !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string[]>? ValidateQuery(
        Guid? customFieldId, string? customFieldValue, Guid? sortCustomFieldId, string? sortDirection)
    {
        var errors = new ValidationErrors();

        ValidateFilter(customFieldId, customFieldValue, errors);

        if (sortDirection is not null
            && !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("sortDirection", "The sort direction must be asc or desc.");
        }

        // A direction with nothing to apply it to would be quietly ignored, and a
        // client that believes it asked for an order it didn't get is the bug this
        // avoids. The default order has no direction to choose.
        if (sortDirection is not null && sortCustomFieldId is null)
        {
            errors.Add("sortCustomFieldId", "A sortDirection needs the sortCustomFieldId it orders by.");
        }

        return errors.ToResultOrNull();
    }

    /// <summary>
    /// The two filter parameters are meaningless apart, so half a filter is a
    /// client error rather than a filter quietly ignored - a list that returns
    /// everything when it was asked to narrow is the kind of bug found late.
    /// </summary>
    private static void ValidateFilter(Guid? customFieldId, string? customFieldValue, ValidationErrors errors)
    {
        if (customFieldId is null && customFieldValue is not null)
        {
            errors.Add("customFieldId", "A customFieldValue needs the customFieldId it applies to.");
        }

        if (customFieldId is not null && customFieldValue is null)
        {
            errors.Add("customFieldValue", "A customFieldId needs the customFieldValue to match.");
        }
    }
}
