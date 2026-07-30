using JobTrail.Modules.Applications.Contracts;

namespace JobTrail.Modules.Analytics.Features.GetCustomFieldChart;

/// <summary>
/// One custom-field panel as a client sees it. The buckets are passed straight
/// through from the module that counted them - this module stores none of it, and
/// re-shaping figures it did not compute would only add a place for them to drift.
/// </summary>
internal sealed record CustomFieldChartResponse(
    Guid DefinitionId,
    string Label,
    string Type,
    int Applications,
    IReadOnlyList<CategoryBucket>? Categories,
    NumberSummary? Numbers,
    IReadOnlyList<PeriodBucket>? Periods)
{
    public static CustomFieldChartResponse From(CustomFieldChart chart) => new(
        chart.DefinitionId,
        chart.Label,
        chart.Type,
        chart.Applications,
        chart.Categories,
        chart.Numbers,
        chart.Periods);
}
