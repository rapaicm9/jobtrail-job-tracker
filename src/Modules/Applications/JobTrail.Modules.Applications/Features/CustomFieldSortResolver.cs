using System.Globalization;
using System.Text.Json;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace JobTrail.Modules.Applications.Features;

/// <summary>Which custom field a list is ordered by, and which way.</summary>
internal sealed record CustomFieldSort(Guid FieldId, bool Descending);

/// <summary>
/// How to order by one custom field: the SQL to sort on, and how to render an
/// answer into the cursor so the next page resumes in the same order.
/// </summary>
/// <param name="Numeric">
/// Whether answers compare as numbers. Only the number type does. Everything else
/// compares as the text <c>-&gt;&gt;</c> yields, which is the right order for each
/// of them: ISO dates sort chronologically as they sort lexically, booleans give
/// false before true, and text is text.
/// </param>
internal sealed record CustomFieldSortPlan(Guid FieldId, bool Descending, bool Numeric)
{
    /// <summary>
    /// The ordering expression, over a parameter for the field id so nothing a
    /// client sent is ever concatenated into SQL.
    /// </summary>
    public string OrderExpression(string fieldIdParameter) =>
        Numeric
            ? $"(a.custom_field_values -> {fieldIdParameter})::numeric"
            : $"a.custom_field_values ->> {fieldIdParameter}";

    /// <summary>
    /// One answer as the cursor records it and the keyset compares it - the same
    /// text <c>-&gt;&gt;</c> would return, or the invariant decimal for a numeric
    /// sort. Null when the application never answered.
    /// </summary>
    public string? Render(CustomFieldValues values)
    {
        if (!values.Values.TryGetValue(FieldId, out var answer))
        {
            return null;
        }

        return Numeric
            ? answer.GetDecimal().ToString(CultureInfo.InvariantCulture)
            : answer.ValueKind switch
            {
                JsonValueKind.String => answer.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => answer.GetRawText(),
            };
    }

    /// <summary>
    /// A rendered answer back as the parameter the comparison needs - a decimal
    /// where the column is cast to numeric, text otherwise. A cursor that cannot
    /// be read as the sort's own type positions nowhere.
    /// </summary>
    public object? ToParameter(string answer) =>
        Numeric
            ? decimal.TryParse(answer, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : null
            : answer;
}

/// <summary>
/// Settles what sorting by a given custom field means, against the definition that
/// says what kind of answers it holds.
/// <para>
/// A multi-select cannot be sorted. There is no defensible order for a set - it
/// would come down to whichever option happened to be written first - so saying so
/// is better than inventing one.
/// </para>
/// </summary>
internal sealed class CustomFieldSortResolver(ApplicationsDbContext dbContext)
{
    public async Task<Result<CustomFieldSortPlan>> ResolveAsync(
        UserId ownerId, CustomFieldSort sort, CancellationToken cancellationToken)
    {
        var definition = await dbContext.CustomFields
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == sort.FieldId && d.OwnerId == ownerId, cancellationToken);

        if (definition is null)
        {
            return CustomFieldErrors.UnknownField(sort.FieldId);
        }

        if (definition.Type is CustomFieldType.MultiSelect)
        {
            return CustomFieldErrors.NotSortable(definition.Label);
        }

        return new CustomFieldSortPlan(
            sort.FieldId, sort.Descending, definition.Type is CustomFieldType.Number);
    }
}
