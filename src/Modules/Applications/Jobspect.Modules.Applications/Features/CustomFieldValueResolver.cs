using System.Globalization;
using System.Text.Json;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Jobspect.Modules.Applications.Features;

/// <summary>
/// Turns the custom-field values a client sent into the bag an application
/// stores, or says why it cannot. This is the one place the answers meet the
/// definitions that give them meaning: the type of a value lives on its
/// definition, so nothing before this point can tell a well-typed answer from a
/// nonsense one.
/// <para>
/// Owner-scoped, so a value aimed at another account's field reads exactly like a
/// value aimed at a field that does not exist - which is the same 404-shaped
/// reasoning the rest of the module uses, expressed as a 422 because the id came
/// in a payload rather than a route.
/// </para>
/// </summary>
internal sealed class CustomFieldValueResolver(ApplicationsDbContext dbContext)
{
    public async Task<Result<CustomFieldValues>> ResolveAsync(
        UserId ownerId,
        IReadOnlyDictionary<Guid, JsonElement> requested,
        CancellationToken cancellationToken)
    {
        // An empty bag needs no definitions, and clearing every value is the
        // commonest edit a client that has none will send.
        if (requested.Count == 0)
        {
            return CustomFieldValues.Empty;
        }

        var definitions = await dbContext.CustomFields
            .AsNoTracking()
            .Where(d => d.OwnerId == ownerId)
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var accepted = new List<KeyValuePair<Guid, JsonElement>>(requested.Count);

        foreach (var (fieldId, value) in requested)
        {
            // An explicit null is "not answered", not an answer of null. Dropping
            // the key keeps the document free of entries that mean nothing, and
            // gives a client one obvious way to clear a single field.
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            if (!definitions.TryGetValue(fieldId, out var definition))
            {
                return CustomFieldErrors.UnknownField(fieldId);
            }

            if (definition.IsArchived)
            {
                return CustomFieldErrors.ArchivedField(definition.Label);
            }

            if (Check(definition, value) is { } error)
            {
                return error;
            }

            accepted.Add(new KeyValuePair<Guid, JsonElement>(fieldId, value));
        }

        return CustomFieldValues.From(accepted);
    }

    /// <summary>
    /// Whether this JSON value is a legitimate answer to this field, or the reason
    /// it is not. The JSON shape carries the type: a number field wants a JSON
    /// number, not the string "12", so a client cannot quietly store the wrong
    /// thing and have a filter fail to find it later.
    /// </summary>
    private static Error? Check(CustomFieldDefinition definition, JsonElement value) => definition.Type switch
    {
        CustomFieldType.Text => Text(definition, value, "a string"),
        CustomFieldType.Url => Url(definition, value),
        CustomFieldType.Number => Number(definition, value),
        CustomFieldType.Checkbox => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? null
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "true or false"),
        CustomFieldType.Date => Date(definition, value),
        CustomFieldType.SingleSelect => SingleSelect(definition, value),
        CustomFieldType.MultiSelect => MultiSelect(definition, value),
        _ => CustomFieldErrors.ValueTypeMismatch(definition.Label, "a supported value"),
    };

    /// <summary>
    /// A number, and one everything downstream can read back.
    /// <para>
    /// JSON's numbers are wider than this system's. <c>1e309</c> is valid JSON and
    /// PostgreSQL would store and order it happily, but the list that sorts by this
    /// field renders every answer through <see cref="JsonElement.GetDecimal"/> to
    /// build the cursor for the next page - so an answer only the database can read
    /// would leave the list sortable by the column and unpageable by the client.
    /// </para>
    /// <para>
    /// The bound therefore goes here, beside the length cap on text and the format on
    /// a date, rather than being absorbed where the cursor is built. Tolerating it
    /// there would be worse than the failure it avoids: the row still sorts among the
    /// answers in SQL, so a cursor that called it unanswered would resume in the
    /// unanswered tail and silently skip everything between.
    /// </para>
    /// </summary>
    private static Error? Number(CustomFieldDefinition definition, JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Number)
        {
            return CustomFieldErrors.ValueTypeMismatch(definition.Label, "a number");
        }

        return value.TryGetDecimal(out _)
            ? null
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "a number no larger than about 7.9e28");
    }

    private static Error? Text(CustomFieldDefinition definition, JsonElement value, string expected)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return CustomFieldErrors.ValueTypeMismatch(definition.Label, expected);
        }

        return value.GetString()!.Length > FieldRules.CustomFieldValueMaxLength
            ? CustomFieldErrors.ValueTypeMismatch(
                definition.Label, $"{FieldRules.CustomFieldValueMaxLength} characters or fewer")
            : null;
    }

    private static Error? Url(CustomFieldDefinition definition, JsonElement value)
    {
        if (Text(definition, value, "a string") is { } error)
        {
            return error;
        }

        return FieldRules.IsAbsoluteHttpUrl(value.GetString()!)
            ? null
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "an absolute http or https URL");
    }

    private static Error? Date(CustomFieldDefinition definition, JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return CustomFieldErrors.ValueTypeMismatch(definition.Label, "a date in yyyy-MM-dd form");
        }

        // Exact, not a lenient parse: a date stored in whatever shape the client
        // happened to send would sort and compare as text later, and "01/02/2026"
        // does not mean the same thing to everyone.
        return DateOnly.TryParseExact(
            value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? null
            : CustomFieldErrors.ValueTypeMismatch(definition.Label, "a date in yyyy-MM-dd form");
    }

    private static Error? SingleSelect(CustomFieldDefinition definition, JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return CustomFieldErrors.ValueTypeMismatch(definition.Label, "one of the field's options");
        }

        var chosen = value.GetString()!;

        // Compared exactly: the options are stored as the user typed them, and an
        // answer that only nearly matches would not group with them in a chart.
        return definition.Options.Contains(chosen, StringComparer.Ordinal)
            ? null
            : CustomFieldErrors.UnknownOption(definition.Label, chosen);
    }

    private static Error? MultiSelect(CustomFieldDefinition definition, JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Array)
        {
            return CustomFieldErrors.ValueTypeMismatch(definition.Label, "an array of the field's options");
        }

        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind is not JsonValueKind.String)
            {
                return CustomFieldErrors.ValueTypeMismatch(definition.Label, "an array of the field's options");
            }

            var chosen = element.GetString()!;
            if (!definition.Options.Contains(chosen, StringComparer.Ordinal))
            {
                return CustomFieldErrors.UnknownOption(definition.Label, chosen);
            }
        }

        return null;
    }
}
