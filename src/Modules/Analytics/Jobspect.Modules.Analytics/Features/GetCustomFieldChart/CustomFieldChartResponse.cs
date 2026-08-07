using Jobspect.Modules.Applications.Contracts;

namespace Jobspect.Modules.Analytics.Features.GetCustomFieldChart;

/// <summary>
/// One custom-field panel as a client sees it. The buckets are passed straight
/// through from the module that counted them - this module stores none of it, and
/// re-shaping figures it did not compute would only add a place for them to drift.
/// <para>
/// <see cref="Type"/> stays a string where the same value is an enum on the
/// module that owns it. It arrives here as a string, because the type is the
/// other module's vocabulary and its published surface trades in data rather than
/// in its own domain types - the events do the same with the work mode. Naming it
/// here would mean either publishing that enum or keeping a second copy of the
/// member list in this module, and a copy would go quietly wrong the first time a
/// field type is added.
/// </para>
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
