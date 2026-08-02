using System.Text.Json;
using Jobspect.Modules.Applications.Domain;
using Jobspect.Modules.Applications.Features.ChartCustomField;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.Modules.Applications.Tests;

/// <summary>
/// Turning recorded answers into the figures a chart is drawn from.
/// <para>
/// The bag is schemaless - the type lives on the definition, not on the answer -
/// so this is the layer where a value of the wrong shape has to be survivable
/// rather than fatal. Nothing here needs a database.
/// </para>
/// </summary>
public sealed class CustomFieldAnswerBucketTests
{
    private static JsonElement? Answer(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static CustomFieldDefinition Definition(CustomFieldType type) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = UserId.New(),
        Label = "Referral source",
        Type = type,
        Options = [],
    };

    // Types travel through the theory as their names: CustomFieldType is internal,
    // and a public xUnit test method may not take an internal type as a parameter -
    // the same reason the stage machine's theories are keyed by name.
    [Theory]
    [InlineData(nameof(CustomFieldType.SingleSelect), true)]
    [InlineData(nameof(CustomFieldType.MultiSelect), true)]
    [InlineData(nameof(CustomFieldType.Number), true)]
    [InlineData(nameof(CustomFieldType.Date), true)]
    [InlineData(nameof(CustomFieldType.Text), false)]
    [InlineData(nameof(CustomFieldType.Url), false)]
    [InlineData(nameof(CustomFieldType.Checkbox), false)]
    public void Only_the_charted_types_are_offered(string type, bool chartable) =>
        CustomFieldAnswerBuckets.IsChartable(Enum.Parse<CustomFieldType>(type)).ShouldBe(chartable);

    [Fact]
    public void A_single_select_counts_each_option_and_keeps_the_unanswered()
    {
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.SingleSelect),
            [Answer("\"Employee\""), Answer("\"Employee\""), Answer("\"Job board\""), null]);

        chart.Applications.ShouldBe(4);
        chart.Numbers.ShouldBeNull();
        chart.Periods.ShouldBeNull();

        // Largest first, and the applications that answered nothing are a bucket of
        // their own - a chart that dropped them would not account for every
        // application beside it. It sorts last whatever its size: it is a residual,
        // not an option competing for rank, and on a tie it would otherwise land in
        // the middle of the legend.
        var categories = chart.Categories.ShouldNotBeNull();
        categories.Select(bucket => (bucket.Value, bucket.Count))
            .ShouldBe([("Employee", 2), ("Job board", 1), (null, 1)]);

        categories.Sum(bucket => bucket.Count).ShouldBe(chart.Applications);
    }

    [Fact]
    public void A_multi_select_counts_one_application_under_every_option_it_chose()
    {
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.MultiSelect),
            [Answer("""["Remote","Equity"]"""), Answer("""["Remote"]"""), null]);

        chart.Applications.ShouldBe(3);

        var categories = chart.Categories.ShouldNotBeNull();
        categories.Single(bucket => bucket.Value == "Remote").Count.ShouldBe(2);
        categories.Single(bucket => bucket.Value == "Equity").Count.ShouldBe(1);
        categories.Single(bucket => bucket.Value == null).Count.ShouldBe(1);

        // Deliberately more than the applications considered, which is why the
        // denominator travels separately.
        categories.Sum(bucket => bucket.Count).ShouldBe(4);
    }

    [Fact]
    public void An_empty_multi_select_answer_counts_as_unanswered()
    {
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.MultiSelect), [Answer("[]"), Answer("""["Remote"]""")]);

        chart.Categories!.Single(bucket => bucket.Value == null).Count.ShouldBe(1);
    }

    [Fact]
    public void A_number_field_is_summarised_by_five_values()
    {
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.Number),
            [Answer("10"), Answer("20"), Answer("30"), Answer("40"), Answer("50")]);

        chart.Categories.ShouldBeNull();

        var numbers = chart.Numbers.ShouldNotBeNull();
        numbers.Answered.ShouldBe(5);
        numbers.Minimum.ShouldBe(10);
        numbers.LowerQuartile.ShouldBe(20);
        numbers.Median.ShouldBe(30);
        numbers.UpperQuartile.ShouldBe(40);
        numbers.Maximum.ShouldBe(50);
    }

    [Fact]
    public void Quartiles_interpolate_rather_than_pick_a_member()
    {
        // Four values: the quartiles fall between them, and rounding to whichever
        // neighbour is nearest would misreport the spread of a small set.
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.Number), [Answer("1"), Answer("2"), Answer("3"), Answer("4")]);

        var numbers = chart.Numbers.ShouldNotBeNull();
        numbers.LowerQuartile.ShouldBe(1.75m);
        numbers.Median.ShouldBe(2.5m);
        numbers.UpperQuartile.ShouldBe(3.25m);
    }

    [Fact]
    public void A_single_number_is_every_one_of_its_own_statistics()
    {
        var chart = CustomFieldAnswerBuckets.Build(Definition(CustomFieldType.Number), [Answer("42")]);

        var numbers = chart.Numbers.ShouldNotBeNull();
        numbers.Answered.ShouldBe(1);
        numbers.Minimum.ShouldBe(42);
        numbers.Median.ShouldBe(42);
        numbers.Maximum.ShouldBe(42);
    }

    [Fact]
    public void A_number_field_nobody_answered_summarises_to_nothing()
    {
        // Null rather than a summary of zeros, which would read as real answers.
        var chart = CustomFieldAnswerBuckets.Build(Definition(CustomFieldType.Number), [null, null]);

        chart.Applications.ShouldBe(2);
        chart.Numbers.ShouldBeNull();
    }

    [Fact]
    public void Dates_are_counted_by_the_month_they_fall_in()
    {
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.Date),
            [Answer("\"2026-03-04\""), Answer("\"2026-03-28\""), Answer("\"2026-04-01\""), null]);

        chart.Periods!.Select(period => (period.PeriodStart, period.Count))
            .ShouldBe([(new DateOnly(2026, 3, 1), 2), (new DateOnly(2026, 4, 1), 1)]);
    }

    [Fact]
    public void An_answer_of_the_wrong_shape_is_counted_as_unanswered_rather_than_thrown_over()
    {
        // Reachable: the bag holds whatever was written against a definition, and a
        // value recorded before a rule tightened outlives it. A chart that failed
        // to draw would be a worse answer than one application in the wrong bucket.
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.SingleSelect),
            [Answer("\"Employee\""), Answer("17"), Answer("true")]);

        var categories = chart.Categories.ShouldNotBeNull();
        categories.Single(bucket => bucket.Value == "Employee").Count.ShouldBe(1);
        categories.Single(bucket => bucket.Value == null).Count.ShouldBe(2);
    }

    [Fact]
    public void An_undated_string_in_a_date_field_is_simply_absent()
    {
        var chart = CustomFieldAnswerBuckets.Build(
            Definition(CustomFieldType.Date), [Answer("\"not a date\""), Answer("\"2026-03-04\"")]);

        chart.Applications.ShouldBe(2);
        chart.Periods.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public void The_definition_travels_with_the_figures_so_a_panel_can_title_itself()
    {
        var definition = Definition(CustomFieldType.SingleSelect);

        var chart = CustomFieldAnswerBuckets.Build(definition, [null]);

        chart.DefinitionId.ShouldBe(definition.Id);
        chart.Label.ShouldBe("Referral source");
        chart.Type.ShouldBe("SingleSelect");
    }
}
