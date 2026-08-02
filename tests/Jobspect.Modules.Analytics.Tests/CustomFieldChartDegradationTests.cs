using Jobspect.Modules.Analytics.Features.GetCustomFieldChart;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Billing.Contracts;
using Jobspect.SharedKernel;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobspect.Modules.Analytics.Tests;

/// <summary>
/// What the custom-field panel does when the module behind it cannot answer.
/// <para>
/// A unit test because it has to be: the integration fixture runs the real host
/// and replaces no services, so a failing dependency cannot be induced over HTTP.
/// The claim is small and worth pinning anyway - this is the only panel on the
/// dashboard served from another module, and the only one that can be down while
/// its neighbours are fine.
/// </para>
/// </summary>
public sealed class CustomFieldChartDegradationTests
{
    private static readonly UserId Owner = UserId.New();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failure_behind_the_panel_is_reported_unavailable_and_never_as_empty()
    {
        var handler = Handler(new ThrowingChartQuery(), entitled: true);

        var result = await handler.HandleAsync(Owner, Guid.CreateVersion7(), campaignId: null, Ct);

        result.IsFailure.ShouldBeTrue();

        // 503, not 500 and emphatically not a 200 with no buckets: an empty chart
        // is a statement about the user's own data that this module is in no
        // position to make.
        result.Error.Type.ShouldBe(ErrorType.Unavailable);
        result.Error.Code.ShouldBe("analytics.chart_unavailable");
    }

    [Fact]
    public async Task A_field_that_is_not_the_callers_is_not_found_rather_than_unavailable()
    {
        // The query returning null is an answer, not a failure - and the two must
        // not be conflated, or a genuine outage would read as "you have no such
        // field" and the user would go looking for data they never lost.
        var handler = Handler(new EmptyChartQuery(), entitled: true);

        var result = await handler.HandleAsync(Owner, Guid.CreateVersion7(), campaignId: null, Ct);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task An_unentitled_caller_is_refused_before_the_other_module_is_troubled()
    {
        var charts = new ThrowingChartQuery();

        var result = await Handler(charts, entitled: false)
            .HandleAsync(Owner, Guid.CreateVersion7(), campaignId: null, Ct);

        result.Error.Type.ShouldBe(ErrorType.Forbidden);

        // And the refusal happened without a cross-module call, which is what keeps
        // an unentitled request from costing another module a query.
        charts.WasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_chart_that_was_built_is_passed_through_unchanged()
    {
        var definitionId = Guid.CreateVersion7();
        var chart = new CustomFieldChart(
            definitionId, "Referral source", "SingleSelect", 3, [new CategoryBucket("Employee", 2)], null, null);

        var result = await Handler(new StubChartQuery(chart), entitled: true)
            .HandleAsync(Owner, definitionId, campaignId: null, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DefinitionId.ShouldBe(definitionId);
        result.Value.Label.ShouldBe("Referral source");
        result.Value.Applications.ShouldBe(3);
        result.Value.Categories.ShouldNotBeNull().ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    private static GetCustomFieldChartHandler Handler(ICustomFieldChartQuery charts, bool entitled) =>
        new(charts, new StubEntitlementQuery(entitled), NullLogger<GetCustomFieldChartHandler>.Instance);

    private sealed class ThrowingChartQuery : ICustomFieldChartQuery
    {
        public bool WasCalled { get; private set; }

        public Task<CustomFieldChart?> GetChartAsync(
            UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("the applications store is not answering");
        }
    }

    private sealed class EmptyChartQuery : ICustomFieldChartQuery
    {
        public Task<CustomFieldChart?> GetChartAsync(
            UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<CustomFieldChart?>(null);
    }

    private sealed class StubChartQuery(CustomFieldChart chart) : ICustomFieldChartQuery
    {
        public Task<CustomFieldChart?> GetChartAsync(
            UserId ownerId, Guid definitionId, Guid? campaignId, CancellationToken cancellationToken) =>
            Task.FromResult<CustomFieldChart?>(chart);
    }

    private sealed class StubEntitlementQuery(bool entitled) : IEntitlementQuery
    {
        public Task<bool> HasEntitlementAsync(
            UserId userId, Entitlement entitlement, CancellationToken cancellationToken) =>
            Task.FromResult(entitled);
    }
}
