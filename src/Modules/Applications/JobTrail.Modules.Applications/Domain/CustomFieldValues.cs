using System.Text.Json;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// The answers one application holds to the fields its owner defined, keyed by
/// <see cref="CustomFieldDefinition"/> id and stored as one JSONB document.
/// <para>
/// Values are kept as the raw JSON scalar the type calls for - a string, a number,
/// a boolean, an array of strings - rather than wrapped in a shape of our own. It
/// is what a person would write by hand, and it is what makes a value reachable by
/// a path: <c>custom_field_values -&gt; '&lt;id&gt;'</c> addresses one field, which
/// is what filtering, sorting and a GIN index over the paths actually queried all
/// need. Wrapping every value would push each of those one level deeper and buy
/// nothing.
/// </para>
/// <para>
/// Deliberately not typed beyond this. The type of an answer lives on the
/// definition, not on the answer, and the aggregate never interprets one - it
/// records what came in and hands it back. Everything is checked once at the edge,
/// against the definitions, and nothing downstream has to ask again.
/// </para>
/// <para>Immutable: an edit replaces the whole bag rather than reaching into it.</para>
/// </summary>
internal sealed class CustomFieldValues
{
    /// <summary>No answers recorded - what a fresh application carries.</summary>
    public static readonly CustomFieldValues Empty = new([]);

    private readonly Dictionary<Guid, JsonElement> _values;

    private string? _json;

    private CustomFieldValues(Dictionary<Guid, JsonElement> values) => _values = values;

    public int Count => _values.Count;

    public IReadOnlyDictionary<Guid, JsonElement> Values => _values;

    public static CustomFieldValues From(IEnumerable<KeyValuePair<Guid, JsonElement>> entries) =>
        new(new Dictionary<Guid, JsonElement>(entries));

    /// <summary>Reads a stored document back. An absent or empty column is no answers.</summary>
    public static CustomFieldValues FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Empty
            : new(JsonSerializer.Deserialize<Dictionary<Guid, JsonElement>>(json) ?? []);

    /// <summary>
    /// The document as it is stored. Cached, since the bag cannot change and the
    /// change tracker asks for it on every comparison.
    /// </summary>
    public string ToJson() => _json ??= JsonSerializer.Serialize(_values);
}
