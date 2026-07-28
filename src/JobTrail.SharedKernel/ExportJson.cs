using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobTrail.SharedKernel;

/// <summary>
/// How every <see cref="IUserDataExporter"/> serializes its contribution, so the
/// sections of one export document read alike however many modules wrote them.
/// <para>
/// camelCase to match the API the same data comes back from, and nulls are
/// written rather than skipped: in an export an absent field and a field with no
/// value are different statements, and only one of them is true.
/// </para>
/// </summary>
public static class ExportJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
