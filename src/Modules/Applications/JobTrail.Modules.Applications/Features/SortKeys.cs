using System.Globalization;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Renders the value a list is sorted by into the cursor's sort key, and reads it
/// back. Every rendering is exact and culture-invariant, because a cursor decoded
/// on the next request has to compare identically to the value still in the column
/// - a lossy rendering would silently skip or repeat the rows around a page edge.
/// <para>
/// The value handed in must be the one the database returned, never one computed
/// locally: a <c>timestamptz</c> is stored to the microsecond, so an instant from
/// the clock would not compare equal to the row it came from.
/// </para>
/// </summary>
internal static class SortKeys
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string From(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static DateOnly? ToDate(string value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>
    /// An instant as its UTC tick count - exact, and ordered the same as the
    /// column, which a formatted string would only be by accident.
    /// </summary>
    public static string From(DateTimeOffset value) => value.UtcTicks.ToString(CultureInfo.InvariantCulture);

    public static DateTimeOffset? ToInstant(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
        && ticks >= 0
        && ticks <= DateTimeOffset.MaxValue.UtcTicks
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : null;

    /// <summary>An application that never answered the field being sorted by.</summary>
    private const string Unanswered = "n:";

    /// <summary>An application that did, with the answer following.</summary>
    private const string AnsweredPrefix = "v:";

    /// <summary>
    /// The position in a custom-field sort, where the answer may be missing
    /// entirely. Unanswered rows sort last, so a page can end inside that group and
    /// the next page has to know it is resuming there rather than among the
    /// answers - and an empty answer is a real answer, so the two cannot both be
    /// the empty string. Hence the marker.
    /// <para>
    /// It stays inside the sort key rather than becoming a third field on the
    /// cursor: the cursor is opaque by contract (ADR-0008), so its payload is free
    /// to carry this without the wire format changing.
    /// </para>
    /// </summary>
    public static string ForAnswer(string? answer) =>
        answer is null ? Unanswered : AnsweredPrefix + answer;

    /// <summary>
    /// Reads that position back: whether the last row had answered, and what it
    /// answered if so. Null when the key was not written by this list.
    /// </summary>
    public static (bool Answered, string Answer)? ToAnswer(string value) => value switch
    {
        Unanswered => (false, string.Empty),
        _ when value.StartsWith(AnsweredPrefix, StringComparison.Ordinal) =>
            (true, value[AnsweredPrefix.Length..]),
        _ => null,
    };
}
