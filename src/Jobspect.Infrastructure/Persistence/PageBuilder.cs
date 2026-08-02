using Jobspect.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Infrastructure.Persistence;

/// <summary>
/// Runs an ordered, already-positioned query and turns it into one page. Shared
/// because the arithmetic is the part that goes wrong: a page has to know whether
/// anything follows it without counting what follows, and every list would
/// otherwise reinvent that off-by-one.
/// </summary>
public static class PageBuilder
{
    /// <summary>
    /// Reads one page from <paramref name="ordered"/> - which must already carry
    /// its ordering and any cursor position - and builds the envelope.
    /// <para>
    /// One row more than asked for is fetched. If it arrives, there is a next page
    /// and the extra row is dropped, so the cursor is built from the last row the
    /// client actually receives. If it doesn't, this is the last page and the
    /// cursor is null - which is why a list whose length is an exact multiple of
    /// the page size doesn't end on a phantom empty page.
    /// </para>
    /// </summary>
    public static async Task<PagedResponse<TResponse>> BuildAsync<TEntity, TResponse>(
        IQueryable<TEntity> ordered,
        int limit,
        Func<TEntity, TResponse> toResponse,
        Func<TEntity, Cursor> toCursor,
        CancellationToken cancellationToken) =>
        FromRows(await ordered.Take(limit + 1).ToListAsync(cancellationToken), limit, toResponse, toCursor);

    /// <summary>
    /// The same envelope, from rows a caller has already read - for a list whose
    /// ordering cannot be expressed in LINQ and so has to fetch its own
    /// <c>limit + 1</c>. It must have asked for one more than it wants, exactly as
    /// <see cref="BuildAsync{TEntity, TResponse}"/> does, or the "is there a next
    /// page" answer is a guess.
    /// <para>
    /// Split out rather than duplicated because the off-by-one is the part that
    /// goes wrong, and it should go wrong in one place or none.
    /// </para>
    /// </summary>
    public static PagedResponse<TResponse> FromRows<TEntity, TResponse>(
        List<TEntity> rows,
        int limit,
        Func<TEntity, TResponse> toResponse,
        Func<TEntity, Cursor> toCursor)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(toCursor);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var nextCursor = hasMore ? toCursor(rows[^1]).Encode() : null;
        return new PagedResponse<TResponse>(rows.Select(toResponse).ToArray(), nextCursor);
    }
}
