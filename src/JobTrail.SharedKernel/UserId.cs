namespace JobTrail.SharedKernel;

/// <summary>
/// Opaque, sortable account identifier. Backed by a UUIDv7 so it orders by
/// creation time, and safe to expose to clients - it carries no personal data.
/// </summary>
public readonly record struct UserId(Guid Value)
{
    /// <summary>Mints a new time-ordered identifier.</summary>
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing value (e.g. one read back from storage or a token).</summary>
    public static UserId From(Guid value) => new(value);

    public static bool TryParse(string? value, out UserId id)
    {
        if (Guid.TryParse(value, out var guid))
        {
            id = new UserId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
