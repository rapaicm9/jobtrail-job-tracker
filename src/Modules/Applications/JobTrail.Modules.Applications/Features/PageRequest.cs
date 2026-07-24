using JobTrail.SharedKernel.Paging;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// How much of a list to read and where to start - the validated form of the
/// <c>limit</c> and <c>cursor</c> query parameters. A null
/// <see cref="Position"/> means the first page.
/// </summary>
internal sealed record PageRequest(int Limit, Cursor? Position);

/// <summary>What a list sorts by, so a cursor meant for one list is not accepted by another.</summary>
internal enum SortKeyKind
{
    Date,
    Instant,
    Text,
}

/// <summary>
/// The paging query parameters, checked and then read - the same validate-then-map
/// split the field rules use, so a handler is handed something already sound.
/// </summary>
internal static class PagingParameters
{
    /// <summary>Enough rows to fill a screen for a client that didn't say.</summary>
    public const int DefaultLimit = 25;

    /// <summary>The ceiling on one page, so a client can't ask for the whole table in one read.</summary>
    public const int MaxLimit = 100;

    /// <summary>
    /// Field-keyed problems with the paging parameters, or <c>null</c> when they
    /// are sound. An out-of-range limit is refused rather than quietly clamped:
    /// silently returning a different page size than the one asked for is the kind
    /// of thing a client only notices much later.
    /// </summary>
    public static Dictionary<string, string[]>? Validate(int? limit, string? cursor, SortKeyKind sortKey)
    {
        var errors = new ValidationErrors();

        if (limit is { } requested && (requested < 1 || requested > MaxLimit))
        {
            errors.Add("limit", $"The limit must be between 1 and {MaxLimit}.");
        }

        // An absent cursor is the first page; a present one that doesn't belong to
        // this list is a client error worth naming. Silently starting again from
        // the top would let a client page the same rows forever without noticing.
        if (!string.IsNullOrEmpty(cursor) && !Positions(cursor, sortKey))
        {
            errors.Add("cursor", "The cursor is not valid. Use the nextCursor returned by a previous page.");
        }

        return errors.ToResultOrNull();
    }

    /// <summary>The parameters as a handler wants them; trusts <see cref="Validate"/> has run.</summary>
    public static PageRequest From(int? limit, string? cursor) =>
        new(limit ?? DefaultLimit, Cursor.Decode(cursor));

    /// <summary>
    /// Whether the cursor decodes and its sort key is the kind this list orders by
    /// - which also rejects a cursor issued by a different list.
    /// </summary>
    private static bool Positions(string cursor, SortKeyKind sortKey) =>
        Cursor.Decode(cursor) is { } position
        && sortKey switch
        {
            SortKeyKind.Date => SortKeys.ToDate(position.SortKey) is not null,
            SortKeyKind.Instant => SortKeys.ToInstant(position.SortKey) is not null,
            _ => true,
        };
}
