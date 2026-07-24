using JobTrail.Modules.Applications.Domain;
using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Features;

/// <summary>
/// Turns validated request fields into the aggregate's values - the same on
/// create and update, so both write slices map a company, compensation, work mode
/// and free text the one way. Each method trusts the shape
/// <see cref="ApplicationFieldValidation"/> already vetted.
/// </summary>
internal static class ApplicationFieldMapping
{
    public static Money? ToMoney(MoneyRequest? compensation) =>
        compensation is null ? null : new Money(compensation.Amount, compensation.Currency!.ToUpperInvariant());

    public static WorkMode? ParseWorkMode(string? workMode) =>
        workMode is null ? null : Enum.Parse<WorkMode>(workMode, ignoreCase: true);

    /// <summary>Trims a free-text field and treats blank as absent, so no field stores "".</summary>
    public static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
