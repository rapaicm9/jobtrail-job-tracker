using JobTrail.Modules.Applications.Features;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace JobTrail.Modules.Applications.Features.ListApplications;

/// <summary>
/// <c>GET /applications?customFieldId=&amp;customFieldValue=</c> - one page of the
/// caller's own applications as list rows, newest first, optionally narrowed to
/// those answering one of the account's custom fields with a given value. Scoped
/// to the token's subject; a user never sees another's. Takes <c>limit</c> and the
/// <c>cursor</c> a previous page returned.
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
        applications.MapGet("", HandleAsync).RequireAuthorization();

    private static async Task<Results<Ok<PagedResponse<ApplicationSummaryResponse>>, ProblemHttpResult>> HandleAsync(
        Guid? customFieldId,
        string? customFieldValue,
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

        if (PagingParameters.Validate(limit, cursor, SortKeyKind.Date) is { } errors)
        {
            return Problems.Validation(errors);
        }

        if (ValidateFilter(customFieldId, customFieldValue) is { } filterErrors)
        {
            return Problems.Validation(filterErrors);
        }

        var filter = customFieldId is { } fieldId ? new CustomFieldFilter(fieldId, customFieldValue!) : null;

        var result = await handler.HandleAsync(
            ownerId, filter, PagingParameters.From(limit, cursor), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();
    }

    /// <summary>
    /// The two filter parameters are meaningless apart, so half a filter is a
    /// client error rather than a filter quietly ignored - a list that returns
    /// everything when it was asked to narrow is the kind of bug found late.
    /// </summary>
    private static Dictionary<string, string[]>? ValidateFilter(Guid? customFieldId, string? customFieldValue)
    {
        var errors = new ValidationErrors();

        if (customFieldId is null && customFieldValue is not null)
        {
            errors.Add("customFieldId", "A customFieldValue needs the customFieldId it applies to.");
        }

        if (customFieldId is not null && customFieldValue is null)
        {
            errors.Add("customFieldValue", "A customFieldId needs the customFieldValue to match.");
        }

        return errors.ToResultOrNull();
    }
}
