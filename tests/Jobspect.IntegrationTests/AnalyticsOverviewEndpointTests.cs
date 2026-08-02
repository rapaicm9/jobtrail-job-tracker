using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Analytics.Features.ProjectApplicationFacts;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The figures every account gets, over the real pipeline: how many applications
/// it has recorded and where they are sitting.
/// <para>
/// The projections are eventually consistent, so the assertions wait rather than
/// assume. What they wait for is stated as a condition on the figures themselves,
/// which is also what keeps a test from passing against a half-filled read model.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsOverviewEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task An_account_with_nothing_recorded_gets_an_empty_dashboard()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        // Empty, not missing - having no applications yet is a normal state, and a
        // 404 here would make a new account look broken.
        var overview = await (await _client.GetAnalyticsOverviewAsync(tokens.AccessToken)).ReadOverviewAsync();

        overview.TotalApplied.ShouldBe(0);
        overview.Pipeline.ShouldBeEmpty();
    }

    [Fact]
    public async Task Applications_are_counted_at_the_stage_they_are_sitting_at()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        await CreateAsync(tokens);
        var moved = await CreateAsync(tokens);
        var advanced = await CreateAsync(tokens);

        await TransitionAsync(tokens, moved, "Screening");
        await TransitionAsync(tokens, advanced, "Screening");
        await TransitionAsync(tokens, advanced, "Interview");

        var overview = await WaitForAsync(tokens, o => o.TotalApplied == 3 && o.Pipeline.Count == 3);

        CountAt(overview, "Applied").ShouldBe(1);
        CountAt(overview, "Screening").ShouldBe(1);
        CountAt(overview, "Interview").ShouldBe(1);

        // The live pipeline reads in the order a person walks it.
        overview.Pipeline.Select(column => column.Stage).ShouldBe(["Applied", "Screening", "Interview"]);
    }

    [Fact]
    public async Task A_closed_application_still_counts_toward_the_total()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var rejected = await CreateAsync(tokens);

        await TransitionAsync(tokens, rejected, "Rejected");

        // "Total applied" is what the account has ever recorded, not what is still
        // open - so an application that ended still counts, under its outcome.
        var overview = await WaitForAsync(tokens, o => CountAt(o, "Rejected") == 1);

        overview.TotalApplied.ShouldBe(1);
        overview.Pipeline.ShouldHaveSingleItem().Stage.ShouldBe("Rejected");
    }

    [Fact]
    public async Task One_account_never_sees_anothers_figures()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        await CreateAsync(mine);
        await CreateAsync(mine);
        await CreateAsync(theirs);

        var ours = await WaitForAsync(mine, o => o.TotalApplied == 2);
        var yours = await WaitForAsync(theirs, o => o.TotalApplied == 1);

        ours.TotalApplied.ShouldBe(2);
        yours.TotalApplied.ShouldBe(1);
    }

    [Fact]
    public async Task The_campaign_filter_narrows_to_one_search()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var defaultCampaign = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);
        var second = await (await _client.CreateCampaignAsync(
            tokens.AccessToken, new { name = "Second search" })).ReadCampaignAsync();

        await CreateAsync(tokens);
        await CreateAsync(tokens, second.Id);
        await CreateAsync(tokens, second.Id);

        await WaitForAsync(tokens, o => o.TotalApplied == 3);

        var inDefault = await ReadAsync(tokens, defaultCampaign);
        var inSecond = await ReadAsync(tokens, second.Id);

        inDefault.TotalApplied.ShouldBe(1);
        inSecond.TotalApplied.ShouldBe(2);

        // Reading is never gated, so the filter carries no entitlement of its own -
        // and an account with one campaign simply gets the same numbers back.
        (await ReadAsync(tokens)).TotalApplied.ShouldBe(3);
    }

    [Fact]
    public async Task A_campaign_that_is_not_the_callers_yields_zeros()
    {
        var mine = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var notMine = await (await _client.CreateCampaignAsync(
            theirs.AccessToken, new { name = "Someone else's search" })).ReadCampaignAsync();

        await CreateAsync(mine);
        await CreateAsync(theirs, notMine.Id);
        await WaitForAsync(mine, o => o.TotalApplied == 1);

        // Zeros rather than a 404: the query is owner-scoped, so a campaign that is
        // not yours is indistinguishable from one holding nothing - which is the
        // right answer and tells the caller nothing about whether it exists.
        var overview = await ReadAsync(mine, notMine.Id);

        overview.TotalApplied.ShouldBe(0);
        overview.Pipeline.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_with_no_stage_yet_counts_but_is_not_a_column()
    {
        // An interview or a campaign move creates the row if it arrives first, and
        // neither carries a stage. It is still an application the account recorded,
        // so it belongs in the total - but null is not a stage, and a client should
        // never be handed one as a column.
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        using (var scope = fixture.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<ApplicationFactsWriter>();
            await new InterviewScheduledProjection(writer).HandleAsync(
                new InterviewScheduled(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    ownerId,
                    DateTimeOffset.UtcNow.AddDays(3),
                    DateTimeOffset.UtcNow),
                Ct);
        }

        var overview = await ReadAsync(tokens);

        overview.TotalApplied.ShouldBe(1);
        overview.Pipeline.ShouldBeEmpty();
    }

    [Fact]
    public async Task It_is_the_free_tier_s_own_figure_and_carries_no_gate()
    {
        // A Free account reaches it. The test exists so that nobody later tidies a
        // Feature: policy onto the route - these two numbers are what the free tier
        // is promised.
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        (await _client.GetPlanAsync(tokens.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await _client.GetAnalyticsOverviewAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_turned_away() =>
        (await _client.GetAnalyticsOverviewAsync(accessToken: null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    private static int CountAt(AnalyticsOverview overview, string stage) =>
        overview.Pipeline.SingleOrDefault(column => column.Stage == stage)?.Count ?? 0;

    private async Task<ApplicationView> CreateAsync(AuthTokens tokens, Guid? campaignId = null) =>
        await (await _client.CreateApplicationAsync(
            tokens.AccessToken,
            campaignId is { } id
                ? new { role = "Engineer", campaignId = id }
                : (object)new { role = "Engineer" })).ReadApplicationAsync();

    private async Task TransitionAsync(AuthTokens tokens, ApplicationView application, string targetStage) =>
        (await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, targetStage))
            .IsSuccessStatusCode.ShouldBeTrue();

    private async Task<AnalyticsOverview> ReadAsync(AuthTokens tokens, Guid? campaignId = null) =>
        await (await _client.GetAnalyticsOverviewAsync(tokens.AccessToken, campaignId)).ReadOverviewAsync();

    private async Task<AnalyticsOverview> WaitForAsync(AuthTokens tokens, Func<AnalyticsOverview, bool> until)
    {
        await Poll.UntilAsync(
            async () => until(await ReadAsync(tokens)),
            "the dashboard should catch up with what the account recorded",
            Ct);

        return await ReadAsync(tokens);
    }
}
