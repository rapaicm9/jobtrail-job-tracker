namespace JobTrail.Modules.Identity.Features;

/// <summary>
/// Accumulates field-keyed validation messages for a slice validator. Fields are
/// keyed by their JSON (camelCase) names so the 422 payload points at what the
/// client actually sent.
/// </summary>
internal sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool IsEmpty => _errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    /// <summary>The accumulated errors, or <c>null</c> when the request is valid.</summary>
    public Dictionary<string, string[]>? ToResultOrNull() =>
        IsEmpty ? null : _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
