using System.Globalization;
using System.Text.Json;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Applications.Domain;

namespace Jobspect.Modules.Applications.Features.ChartCustomField;

/// <summary>
/// Turns the answers recorded against one definition into the figures a chart is
/// drawn from.
/// <para>
/// The bag is schemaless by design - the type lives on the definition, not on the
/// answer - so nothing here trusts what it finds. An answer of the wrong shape is
/// counted as unanswered rather than thrown over: a field whose definition
/// out-lived a change of mind, or a value written before a rule tightened, is
/// reachable, and a chart that fails to draw is a worse answer than a chart that
/// leaves one application out of a bucket.
/// </para>
/// </summary>
internal static class CustomFieldAnswerBuckets
{
    /// <summary>Whether this kind of field can be charted at all.</summary>
    public static bool IsChartable(CustomFieldType type) =>
        type is CustomFieldType.SingleSelect or CustomFieldType.MultiSelect
            or CustomFieldType.Number or CustomFieldType.Date;

    /// <summary>
    /// The chart for one definition over <paramref name="answers"/>, one entry per
    /// application considered - null where the application did not answer.
    /// </summary>
    public static CustomFieldChart Build(
        CustomFieldDefinition definition, IReadOnlyList<JsonElement?> answers) =>
        definition.Type switch
        {
            CustomFieldType.SingleSelect => Chart(definition, answers, categories: SingleSelect(answers)),
            CustomFieldType.MultiSelect => Chart(definition, answers, categories: MultiSelect(answers)),
            CustomFieldType.Number => Chart(definition, answers, numbers: Numbers(answers)),
            CustomFieldType.Date => Chart(definition, answers, periods: Months(answers)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(definition), $"{definition.Type} is not chartable; callers check first."),
        };

    private static CustomFieldChart Chart(
        CustomFieldDefinition definition,
        IReadOnlyList<JsonElement?> answers,
        IReadOnlyList<CategoryBucket>? categories = null,
        NumberSummary? numbers = null,
        IReadOnlyList<PeriodBucket>? periods = null) =>
        new(definition.Id, definition.Label, definition.Type.ToString(), answers.Count,
            categories, numbers, periods);

    /// <summary>
    /// One bucket per option chosen, largest first, with the applications that
    /// answered nothing gathered under a null value.
    /// </summary>
    private static CategoryBucket[] SingleSelect(IReadOnlyList<JsonElement?> answers) =>
        Tally(answers.Select(AsOption));

    /// <summary>
    /// The same, except an application answering several options is counted under
    /// each - so these do not sum to the applications considered, and the chart
    /// carries that denominator separately.
    /// </summary>
    private static CategoryBucket[] MultiSelect(IReadOnlyList<JsonElement?> answers) =>
        Tally(answers.SelectMany(AsOptions));

    /// <summary>
    /// Options by how many chose them, largest first.
    /// <para>
    /// The unanswered bucket always sorts last, whatever its size. It is a
    /// residual rather than a category competing for rank, and letting it place on
    /// count alone drops "not answered" into the middle of a chart's legend
    /// wherever the counts happen to tie.
    /// </para>
    /// </summary>
    private static CategoryBucket[] Tally(IEnumerable<string?> options) =>
        [.. options
            .GroupBy(option => option, StringComparer.Ordinal)
            .Select(group => new CategoryBucket(group.Key, group.Count()))
            .OrderBy(bucket => bucket.Value is null)
            .ThenByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Value, StringComparer.Ordinal)];

    private static string? AsOption(JsonElement? answer) =>
        answer is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>
    /// The options one application chose. A multi-select answer is an array; an
    /// application that answered nothing still contributes one null, so it is
    /// counted once in the unanswered bucket rather than disappearing.
    /// </summary>
    private static IEnumerable<string?> AsOptions(JsonElement? answer)
    {
        if (answer is not { ValueKind: JsonValueKind.Array } array || array.GetArrayLength() == 0)
        {
            return [null];
        }

        var chosen = array.EnumerateArray()
            .Where(element => element.ValueKind is JsonValueKind.String)
            .Select(element => element.GetString())
            .ToArray();

        return chosen.Length > 0 ? chosen : [null];
    }

    /// <summary>
    /// The five-number summary of the answers that are numbers. Null when nobody
    /// answered - there is nothing to summarise, and zeros would read as real.
    /// </summary>
    private static NumberSummary? Numbers(IReadOnlyList<JsonElement?> answers)
    {
        var values = answers
            .Where(answer => answer is { ValueKind: JsonValueKind.Number })
            .Select(answer => answer!.Value.TryGetDecimal(out var number) ? number : (decimal?)null)
            .Where(number => number is not null)
            .Select(number => number!.Value)
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        var (min, lower, median, upper, max) = AnswerStatistics.Summarise(values);
        return new NumberSummary(values.Length, min, lower, median, upper, max);
    }

    /// <summary>
    /// Counts per month, keyed by the first of it, oldest first. Applications that
    /// did not answer are absent rather than gathered into a bucket of their own -
    /// a date axis has no place to put them.
    /// </summary>
    private static PeriodBucket[] Months(IReadOnlyList<JsonElement?> answers) =>
        [.. answers
            .Select(AsDate)
            .Where(date => date is not null)
            .GroupBy(date => new DateOnly(date!.Value.Year, date.Value.Month, 1))
            .OrderBy(month => month.Key)
            .Select(month => new PeriodBucket(month.Key, month.Count()))];

    private static DateOnly? AsDate(JsonElement? answer) =>
        answer is { ValueKind: JsonValueKind.String } value
        && DateOnly.TryParse(
            value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
