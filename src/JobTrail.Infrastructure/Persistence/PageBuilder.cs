using JobTrail.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Infrastructure.Persistence;

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
        CancellationToken cancellationToken)
    {
        var rows = await ordered.Take(limit + 1).ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var nextCursor = hasMore ? toCursor(rows[^1]).Encode() : null;
        return new PagedResponse<TResponse>(rows.Select(toResponse).ToArray(), nextCursor);
    }
}
