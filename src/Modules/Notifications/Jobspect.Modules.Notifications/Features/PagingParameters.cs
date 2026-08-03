using System.Globalization;
using Jobspect.SharedKernel.Paging;

namespace Jobspect.Modules.Notifications.Features;

/// <summary>
/// How much of the feed to read and where to start - the validated form of the
/// <c>limit</c> and <c>cursor</c> query parameters. A null <see cref="Position"/>
/// means the first page.
/// </summary>
internal sealed record PageRequest(int Limit, Cursor? Position);

/// <summary>
/// The paging query parameters, checked and then read.
/// <para>
/// <b>A deliberate copy of the Applications module's, trimmed.</b> The envelope and
/// the cursor codec are shared in the kernel because a wire contract must not drift
/// (ADR 0008); this is the edge validation that sits over them, and it follows the
/// same per-module rule as <see cref="Problems"/> and <see cref="Caller"/>. What is
/// dropped is the sort-key <em>kind</em>: that module has four ways to order a list
/// and has to refuse a cursor issued under a different one, while the feed orders
/// exactly one way and always will - a reminder feed read in any order but newest
/// first is not a feed.
/// </para>
/// <para>
/// The limits below are the same numbers, and a test holds them against the other
/// module's so the copy cannot drift silently.
/// </para>
/// </summary>
internal static class PagingParameters
{
    /// <summary>Enough rows to fill a screen for a client that didn't say.</summary>
    public const int DefaultLimit = 25;

    /// <summary>The ceiling on one page, so a client can't ask for the whole feed in one read.</summary>
    public const int MaxLimit = 100;

    /// <summary>
    /// Field-keyed problems with the paging parameters, or <c>null</c> when they are
    /// sound. An out-of-range limit is refused rather than quietly clamped: silently
    /// returning a different page size than the one asked for is the kind of thing a
    /// client only notices much later.
    /// </summary>
    public static Dictionary<string, string[]>? Validate(int? limit, string? cursor)
    {
        var errors = new ValidationErrors();

        if (limit is { } requested && (requested < 1 || requested > MaxLimit))
        {
            errors.Add("limit", $"The limit must be between 1 and {MaxLimit}.");
        }

        // An absent cursor is the first page; a present one that does not decode is a
        // client error worth naming. Silently starting again from the top would let a
        // client page the same rows forever without noticing.
        if (!string.IsNullOrEmpty(cursor) && !Positions(cursor))
        {
            errors.Add("cursor", "The cursor is not valid. Use the nextCursor returned by a previous page.");
        }

        return errors.ToResultOrNull();
    }

    /// <summary>The parameters as a handler wants them; trusts <see cref="Validate"/> has run.</summary>
    public static PageRequest From(int? limit, string? cursor) =>
        new(limit ?? DefaultLimit, Cursor.Decode(cursor));

    /// <summary>
    /// An instant as its UTC tick count - exact, and ordered the same as the column,
    /// which a formatted string would only be by accident.
    /// </summary>
    public static string SortKeyFrom(DateTimeOffset value) =>
        value.UtcTicks.ToString(CultureInfo.InvariantCulture);

    public static DateTimeOffset? SortKeyToInstant(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
        && ticks >= 0
        && ticks <= DateTimeOffset.MaxValue.UtcTicks
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    /// <summary>Whether the cursor decodes and carries the instant this list orders by.</summary>
    private static bool Positions(string cursor) =>
        Cursor.Decode(cursor) is { } position && SortKeyToInstant(position.SortKey) is not null;
}
