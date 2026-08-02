using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Analytics.Features.ProjectApplicationFacts;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.SharedKernel;
using Jobspect.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The paid figures that a job search driven over HTTP cannot produce, read back
/// through the real endpoint.
/// <para>
/// Every other insights test creates applications and moves them, which is the
/// right way to prove the wiring - but everything it does happens now. A
/// time-to-first-response of five days needs a response that occurred five days
/// ago, and there is no clock in the host to move. So the base rows are seeded by
/// handing the projections events with the timestamps the figures are supposed to
/// measure, and the assertion is made against <c>GET /analytics/insights</c> for
/// the account those rows belong to.
/// </para>
/// <para>
/// Timestamps sit at midnight so the elapsed spans are whole days: the applied
/// date is read as midnight UTC, and a fractional remainder here would only make
/// the expected values harder to read.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsSeededMetricsTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Applied = new(2026, 3, 1);

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task The_median_time_to_a_first_response_is_the_middle_answer_and_says_how_many_it_had()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var owner = UserId.From(tokens.UserId);

        // Three answered at three, five and forty days, and one never answered at
        // all. The mean of the three would be sixteen days - a figure describing
        // nothing that happened to this account - which is the whole reason the
        // response is a median.
        await SeedAsync(owner, respondedAfterDays: 3);
        await SeedAsync(owner, respondedAfterDays: 5);
        await SeedAsync(owner, respondedAfterDays: 40);
        await SeedAsync(owner, respondedAfterDays: null);

        var insights = await ReadAsync(tokens);

        insights.Timing.MedianDaysToFirstResponse!.Value.ShouldBe(5, 0.0001);

        // The unanswered one is absent from the median rather than a zero inside
        // it, and the sample count is what lets a client see the median rests on
        // three applications and not four.
        insights.Timing.FirstResponseSamples.ShouldBe(3);
        insights.Funnel.Total.ShouldBe(4);
        insights.Rates.Response!.Value.ShouldBe(0.75, 0.0001);
    }

    [Fact]
    public async Task An_even_number_of_offers_takes_the_midpoint_of_the_middle_two()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var owner = UserId.From(tokens.UserId);

        await SeedAsync(owner, respondedAfterDays: 10, offeredAfterDays: 10);
        await SeedAsync(owner, respondedAfterDays: 20, offeredAfterDays: 20);

        var insights = await ReadAsync(tokens);

        insights.Timing.MedianDaysToOffer!.Value.ShouldBe(15, 0.0001);
        insights.Timing.OfferSamples.ShouldBe(2);
    }

    [Fact]
    public async Task An_application_whose_submission_never_arrived_is_measured_by_nothing()
    {
        // The nullable applied date, seen from the far end. A row created by a
        // stage change alone has no date to measure from, so it counts toward the
        // funnel and contributes to no duration - rather than being measured from
        // some default and reporting a span nobody experienced.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var owner = UserId.From(tokens.UserId);

        await SeedAsync(owner, respondedAfterDays: 4);
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), Guid.CreateVersion7(), owner, "Applied", "Screening", Midnight(9)));

        var insights = await ReadAsync(tokens);

        insights.Funnel.Total.ShouldBe(2);
        insights.Funnel.Responded.ShouldBe(2);

        // Two responses, one measurable span.
        insights.Timing.MedianDaysToFirstResponse!.Value.ShouldBe(4, 0.0001);
        insights.Timing.FirstResponseSamples.ShouldBe(1);
    }

    /// <summary>
    /// One application applied for on <see cref="Applied"/>, optionally answered
    /// and optionally taken to an offer the given number of days later.
    /// </summary>
    private async Task SeedAsync(UserId owner, int? respondedAfterDays, int? offeredAfterDays = null)
    {
        var id = Guid.CreateVersion7();

        await ApplyAsync(new ApplicationSubmitted(
            Guid.CreateVersion7(), id, owner, Guid.CreateVersion7(), CompanyId: null,
            Applied, "Referral", "Remote", Midnight(0)));

        if (respondedAfterDays is { } responded)
        {
            await ApplyAsync(new ApplicationStageChanged(
                Guid.CreateVersion7(), id, owner, "Applied", "Screening", Midnight(responded)));
        }

        if (offeredAfterDays is { } offered)
        {
            await ApplyAsync(new ApplicationStageChanged(
                Guid.CreateVersion7(), id, owner, "Screening", "Offer", Midnight(offered)));
        }
    }

    private static DateTimeOffset Midnight(int daysAfterApplying) =>
        new DateTimeOffset(Applied.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(daysAfterApplying);

    /// <summary>
    /// Hands one event to the projection that handles it, as the projection tests
    /// do - constructed rather than resolved, so a handler registered elsewhere
    /// cannot quietly change what is being seeded.
    /// </summary>
    private async Task ApplyAsync(IIntegrationEvent integrationEvent)
    {
        using var scope = fixture.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ApplicationFactsWriter>();

        Task handled = integrationEvent switch
        {
            ApplicationSubmitted e => new ApplicationSubmittedProjection(writer).HandleAsync(e, Ct),
            ApplicationStageChanged e => new ApplicationStageChangedProjection(writer).HandleAsync(e, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(integrationEvent)),
        };

        await handled;
    }

    /// <summary>
    /// No polling: these rows were written directly rather than delivered, so there
    /// is nothing in flight to wait for.
    /// </summary>
    private async Task<AnalyticsInsights> ReadAsync(AuthTokens tokens) =>
        await (await _client.GetAnalyticsInsightsAsync(tokens.AccessToken)).ReadInsightsAsync();
}
