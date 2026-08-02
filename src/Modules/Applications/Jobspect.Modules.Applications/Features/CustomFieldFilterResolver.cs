using System.Globalization;
using System.Text.Json;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features;

/// <summary>Which custom field a list is being filtered by, and to what.</summary>
internal sealed record CustomFieldFilter(Guid FieldId, string Value);

/// <summary>
/// Turns "this field equals this" into the JSON document a containment test
/// compares against - <c>custom_field_values @&gt; {"&lt;id&gt;": &lt;value&gt;}</c>.
/// <para>
/// The coercion is the whole job, and it is not cosmetic. A filter value arrives
/// from a query string as text, but containment compares JSON to JSON: the probe
/// <c>{"id":"3"}</c> does not match a stored <c>3</c>, and a client filtering a
/// number field would get a confidently empty page instead of an error. So the
/// definition is read first and the text becomes the JSON scalar that field's type
/// actually stores.
/// </para>
/// <para>
/// Archived fields filter like any other. They hold real answers that are still
/// read back, and refusing to search data the user can plainly see would be a
/// strange kind of tidiness.
/// </para>
/// </summary>
internal sealed class CustomFieldFilterResolver(ApplicationsDbContext dbContext)
{
    public async Task<Result<string>> ResolveAsync(
        UserId ownerId, CustomFieldFilter filter, CancellationToken cancellationToken)
    {
        var definition = await dbContext.CustomFields
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == filter.FieldId && d.OwnerId == ownerId, cancellationToken);

        // Owner-scoped, so another account's field reads exactly like one that
        // never existed - the same reasoning the rest of the module uses.
        if (definition is null)
        {
            return CustomFieldErrors.UnknownField(filter.FieldId);
        }

        var probe = ToProbe(definition, filter.Value);
        if (probe.IsFailure)
        {
            return probe.Error;
        }

        return JsonSerializer.Serialize(
            new Dictionary<string, object?> { [filter.FieldId.ToString()] = probe.Value });
    }

    /// <summary>
    /// The filter text as the JSON value this field stores, or why it cannot be
    /// one. A multi-select is probed with a one-element array, since containment
    /// on a JSON array asks "is this among them" - which is what filtering a
    /// multi-select means.
    /// </summary>
    private static Result<object?> ToProbe(CustomFieldDefinition definition, string value) => definition.Type switch
    {
        CustomFieldType.Text or CustomFieldType.Url or CustomFieldType.SingleSelect => value,

        CustomFieldType.Number => decimal.TryParse(
            value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "a number"),

        CustomFieldType.Checkbox => bool.TryParse(value, out var flag)
            ? flag
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "true or false"),

        // Re-rendered from the parsed date rather than passed through, so the probe
        // is the exact text the column holds whatever the client sent.
        CustomFieldType.Date => DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "a date in yyyy-MM-dd form"),

        CustomFieldType.MultiSelect => new[] { value },

        _ => CustomFieldErrors.ValueTypeMismatch(definition.Label, "a supported value"),
    };
}
