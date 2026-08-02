using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The paid dashboard end to end: the gate, and the figures behind it.
/// <para>
/// The arithmetic has its own unit tests, where the awkward cases can be
/// enumerated. What is proved here is what those cannot be: that a real pipeline
/// walked over HTTP produces the figures, that the gate holds at the edge, and
/// that an account with nothing recorded is told so rather than shown zeros.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsInsightsEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task A_free_account_is_refused_and_a_pro_account_is_not()
    {
        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var pro = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        (await _client.GetAnalyticsInsightsAsync(free.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.GetAnalyticsInsightsAsync(pro.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.GetAnalyticsInsightsAsync(accessToken: null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_free_account_still_reaches_its_own_figures()
    {
        // The line the gate draws. What is sold is the analysis, not the account's
        // record of its own job search - so the free figures stay open to the very
        // account the paid ones just refused.
        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        (await _client.GetAnalyticsInsightsAsync(free.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.GetAnalyticsOverviewAsync(free.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.ListApplicationsAsync(free.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_account_with_nothing_recorded_is_told_so_rather_than_shown_zeros()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        var insights = await ReadAsync(tokens);

        insights.Funnel.Total.ShouldBe(0);

        // Null, not zero. A brand-new account has no response rate, and 0% is a
        // number the reader would believe.
        insights.Rates.Response.ShouldBeNull();
        insights.Rates.Interview.ShouldBeNull();
        insights.Rates.Offer.ShouldBeNull();
        insights.Timing.MedianDaysToFirstResponse.ShouldBeNull();
        insights.Timing.FirstResponseSamples.ShouldBe(0);
        insights.Timing.TimeInStage.ShouldBeEmpty();
        insights.Trend.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_funnel_counts_how_far_each_application_got()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        var untouched = await CreateAsync(tokens);
        var screened = await CreateAsync(tokens);
        var interviewed = await CreateAsync(tokens);
        var skipped = await CreateAsync(tokens);

        await TransitionAsync(tokens, screened, "Screening");
        await TransitionAsync(tokens, interviewed, "Screening");
        await TransitionAsync(tokens, interviewed, "Interview");

        // Straight to Offer - a legal forward skip, and the case a funnel read off
        // the current stage gets wrong in both directions.
        await TransitionAsync(tokens, skipped, "Offer");

        var insights = await WaitForAsync(tokens, i => i.Funnel.Total == 4 && i.Funnel.ReachedOffer == 1);

        insights.Funnel.Total.ShouldBe(4);
        insights.Funnel.Responded.ShouldBe(3);
        insights.Funnel.ReachedScreening.ShouldBe(2);
        insights.Funnel.ReachedInterview.ShouldBe(1);
        insights.Funnel.ReachedOffer.ShouldBe(1);

        // The skipped application never had an interview, and says so.
        insights.Funnel.ReachedInterview.ShouldBeLessThan(insights.Funnel.ReachedOffer + 1);

        // Every rate is over the total applied, so a client never has to guess.
        insights.Rates.Response!.Value.ShouldBe(0.75, 0.0001);
        insights.Rates.Interview!.Value.ShouldBe(0.25, 0.0001);
        insights.Rates.Offer!.Value.ShouldBe(0.25, 0.0001);

        _ = untouched;
    }

    [Fact]
    public async Task A_rejection_counts_as_a_response_and_being_ghosted_does_not()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        var rejected = await CreateAsync(tokens);
        var ghosted = await CreateAsync(tokens);
        var withdrawn = await CreateAsync(tokens);

        await TransitionAsync(tokens, rejected, "Rejected");
        await TransitionAsync(tokens, ghosted, "Ghosted");
        await TransitionAsync(tokens, withdrawn, "Withdrawn");

        var insights = await WaitForAsync(tokens, i => i.Funnel.Total == 3 && i.Funnel.Responded == 1);

        // Silence is not an answer, and neither is the user's own withdrawal.
        insights.Funnel.Responded.ShouldBe(1);
        insights.Rates.Response!.Value.ShouldBe(1d / 3, 0.0001);
    }

    [Fact]
    public async Task Time_in_stage_measures_only_intervals_that_ended()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        var moved = await CreateAsync(tokens);
        await CreateAsync(tokens);

        await TransitionAsync(tokens, moved, "Screening");

        var insights = await WaitForAsync(
            tokens, i => i.Timing.TimeInStage.Any(stage => stage.Stage == "Applied"));

        // One application left Applied, so Applied has exactly one sample; the
        // other is still sitting there and contributes nothing. Nothing has left
        // Screening at all.
        var applied = insights.Timing.TimeInStage.Single(stage => stage.Stage == "Applied");
        applied.Samples.ShouldBe(1);
        insights.Timing.TimeInStage.ShouldNotContain(stage => stage.Stage == "Screening");
    }

    [Fact]
    public async Task Breakdowns_keep_the_applications_that_recorded_nothing()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        await CreateAsync(tokens, source: "LinkedIn");
        await CreateAsync(tokens, source: "LinkedIn");
        await CreateAsync(tokens, source: "Referral");
        await CreateAsync(tokens);

        var insights = await WaitForAsync(tokens, i => i.Funnel.Total == 4);

        // The not-recorded slice is present and named null. Unlike the pipeline
        // snapshot's missing stage, this is a fact about what the user filled in,
        // and a breakdown that hid it would not add up to the total beside it.
        insights.Breakdowns.Source.Sum(slice => slice.Count).ShouldBe(4);
        insights.Breakdowns.Source.ShouldContain(slice => slice.Value == null && slice.Count == 1);

        // Largest slice first.
        insights.Breakdowns.Source[0].Value.ShouldBe("LinkedIn");
        insights.Breakdowns.Source[0].Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_trend_buckets_applications_by_the_monday_of_their_week()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        // A Tuesday and the Sunday that closes the same ISO week, plus one in the
        // week after.
        await CreateAsync(tokens, appliedDate: "2026-03-03");
        await CreateAsync(tokens, appliedDate: "2026-03-08");
        await CreateAsync(tokens, appliedDate: "2026-03-09");

        var insights = await WaitForAsync(tokens, i => i.Trend.Count == 2);

        insights.Trend[0].WeekStarting.ShouldBe(new DateOnly(2026, 3, 2));
        insights.Trend[0].Count.ShouldBe(2);
        insights.Trend[1].WeekStarting.ShouldBe(new DateOnly(2026, 3, 9));
        insights.Trend[1].Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_campaign_filter_narrows_the_paid_figures_too()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var second = await (await _client.CreateCampaignAsync(
            tokens.AccessToken, new { name = "Second search" })).ReadCampaignAsync();

        await CreateAsync(tokens);
        await CreateAsync(tokens, campaignId: second.Id);

        await WaitForAsync(tokens, i => i.Funnel.Total == 2);

        (await ReadAsync(tokens, second.Id)).Funnel.Total.ShouldBe(1);
    }

    [Fact]
    public async Task One_account_never_sees_anothers_figures()
    {
        var mine = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        await CreateAsync(mine);
        await CreateAsync(theirs);
        await CreateAsync(theirs);

        (await WaitForAsync(mine, i => i.Funnel.Total == 1)).Funnel.Total.ShouldBe(1);
        (await WaitForAsync(theirs, i => i.Funnel.Total == 2)).Funnel.Total.ShouldBe(2);
    }

    private async Task<ApplicationView> CreateAsync(
        AuthTokens tokens, Guid? campaignId = null, string? source = null, string? appliedDate = null)
    {
        var body = new Dictionary<string, object?> { ["role"] = "Engineer" };

        if (campaignId is { } id)
        {
            body["campaignId"] = id;
        }

        if (source is not null)
        {
            body["source"] = source;
        }

        if (appliedDate is not null)
        {
            body["appliedDate"] = appliedDate;
        }

        return await (await _client.CreateApplicationAsync(tokens.AccessToken, body)).ReadApplicationAsync();
    }

    private async Task TransitionAsync(AuthTokens tokens, ApplicationView application, string targetStage) =>
        (await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, targetStage))
            .IsSuccessStatusCode.ShouldBeTrue();

    private async Task<AnalyticsInsights> ReadAsync(AuthTokens tokens, Guid? campaignId = null) =>
        await (await _client.GetAnalyticsInsightsAsync(tokens.AccessToken, campaignId)).ReadInsightsAsync();

    private async Task<AnalyticsInsights> WaitForAsync(AuthTokens tokens, Func<AnalyticsInsights, bool> until)
    {
        await Poll.UntilAsync(
            async () => until(await ReadAsync(tokens)),
            "the paid dashboard should catch up with what the account recorded",
            Ct);

        return await ReadAsync(tokens);
    }
}
