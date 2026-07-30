using System.Net;
using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The weekly goal end to end: who may set one, who may read and clear one, and
/// which applications count toward it.
/// <para>
/// The week arithmetic has its own unit tests, where a fixed date can be handed in.
/// There is no fake clock in the host, so nothing here asserts what today is -
/// instead every date a test needs is derived from the <c>weekStart</c> the API
/// itself returned. That is what keeps a run started at 23:59 on a Sunday from
/// failing on a boundary the test invented for itself.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsWeeklyGoalTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Only_a_pro_account_may_set_a_goal()
    {
        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var pro = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        (await _client.SetWeeklyGoalAsync(free.AccessToken, new { target = 8 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _client.SetWeeklyGoalAsync(pro.AccessToken, new { target = 8 }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.SetWeeklyGoalAsync(accessToken: null, new { target = 8 }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reading_and_clearing_a_goal_are_open_to_every_tier()
    {
        // The line the gate draws. Setting a target is the capability that is sold;
        // the number the user typed is their own record, and so is getting rid of
        // it. A free account has no goal to see, and is still not refused the ask.
        var free = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        (await _client.GetWeeklyGoalAsync(free.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _client.ClearWeeklyGoalAsync(free.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Setting_a_goal_answers_with_the_goal_and_the_week()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        var set = await (await _client.SetWeeklyGoalAsync(tokens.AccessToken, new { target = 8 }))
            .ReadWeeklyGoalAsync();

        set.Target.ShouldBe(8);
        set.Applied.ShouldBe(0);

        // The week is always reported, and always a Monday - a client cannot work
        // it out for itself, since it depends on the timezone the server holds.
        set.WeekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        // A following read says exactly what the write said, so no client needs to
        // make one.
        (await ReadAsync(tokens)).ShouldBe(set);
    }

    [Fact]
    public async Task Changing_a_goal_replaces_it_rather_than_adding_a_second()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        await SetAsync(tokens, 8);
        await SetAsync(tokens, 3);

        (await ReadAsync(tokens)).Target.ShouldBe(3);
    }

    [Fact]
    public async Task An_account_with_no_goal_is_not_shown_a_bare_weekly_count()
    {
        // Progress is progress toward something. With no target it would be a plain
        // count of this week's applications, which is a paid figure the trend on
        // /insights already sells - so it is withheld rather than volunteered.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        await CreateAsync(tokens);

        var goal = await ReadAsync(tokens);

        goal.Target.ShouldBeNull();
        goal.Applied.ShouldBeNull();
        goal.WeekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact]
    public async Task Progress_counts_the_applications_applied_inside_this_week()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var weekStart = (await SetAsync(tokens, 8)).WeekStart;

        // Both ends of the week, and the day before it. Dates are taken from the
        // week the API reported rather than from this machine's clock, so the test
        // means the same thing whenever it runs.
        await CreateAsync(tokens, weekStart);
        await CreateAsync(tokens, weekStart.AddDays(6));
        await CreateAsync(tokens, weekStart.AddDays(-1));

        // Backdating is the user telling us when they applied, so an application
        // entered today for last week belongs to last week's effort.
        var goal = await WaitForAsync(tokens, g => g.Applied == 2);

        goal.Target.ShouldBe(8);
        goal.Applied.ShouldBe(2);
    }

    [Fact]
    public async Task One_account_never_sees_anothers_progress()
    {
        var mine = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var theirs = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        var weekStart = (await SetAsync(mine, 5)).WeekStart;
        await SetAsync(theirs, 5);

        await CreateAsync(theirs, weekStart);
        await CreateAsync(theirs, weekStart);

        await WaitForAsync(theirs, g => g.Applied == 2);

        (await ReadAsync(mine)).Applied.ShouldBe(0);
    }

    [Fact]
    public async Task A_downgraded_account_keeps_its_goal_and_can_still_be_rid_of_it()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var weekStart = (await SetAsync(tokens, 8)).WeekStart;
        await CreateAsync(tokens, weekStart);
        await WaitForAsync(tokens, g => g.Applied == 1);

        // The same account without the entitlement is what a downgrade looks like.
        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        // Everything it recorded is still its own to see, progress included.
        var kept = await ReadAsync(tokens);
        kept.Target.ShouldBe(8);
        kept.Applied.ShouldBe(1);

        // What stops is raising or changing it.
        (await _client.SetWeeklyGoalAsync(tokens.AccessToken, new { target = 12 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And the way back to the free shape stays open. Gating this is the trap:
        // it would leave the account holding a goal it could neither change nor
        // drop.
        (await _client.ClearWeeklyGoalAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync(tokens)).Target.ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_a_goal_that_was_never_set_is_not_an_error()
    {
        // At-least-once is a client property too: a mobile client retrying a dropped
        // response asked to hold no goal and holds none.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        (await _client.ClearWeeklyGoalAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.ClearWeeklyGoalAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(101)]
    public async Task A_target_outside_the_allowed_range_is_refused(int target)
    {
        // Zero among them, and deliberately: tracking no goal is the absence of the
        // goal, so there is one way to say it and it is DELETE.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        await (await _client.SetWeeklyGoalAsync(tokens.AccessToken, new { target }))
            .ShouldBeValidationProblemAsync("target");
    }

    [Fact]
    public async Task A_request_with_no_target_at_all_is_refused()
    {
        // Nullable on the wire so an omitted field is a question, not a silent zero.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

        await (await _client.SetWeeklyGoalAsync(tokens.AccessToken, new { }))
            .ShouldBeValidationProblemAsync("target");
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_turned_away()
    {
        (await _client.GetWeeklyGoalAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.ClearWeeklyGoalAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<WeeklyGoalView> SetAsync(AuthTokens tokens, int target) =>
        await (await _client.SetWeeklyGoalAsync(tokens.AccessToken, new { target })).ReadWeeklyGoalAsync();

    private async Task<WeeklyGoalView> ReadAsync(AuthTokens tokens) =>
        await (await _client.GetWeeklyGoalAsync(tokens.AccessToken)).ReadWeeklyGoalAsync();

    private async Task CreateAsync(AuthTokens tokens, DateOnly? appliedDate = null) =>
        (await _client.CreateApplicationAsync(
            tokens.AccessToken,
            appliedDate is { } date
                ? new { role = "Engineer", appliedDate = date.ToString("O") }
                : (object)new { role = "Engineer" })).IsSuccessStatusCode.ShouldBeTrue();

    /// <summary>
    /// The read model is filled from events, so progress arrives a moment after the
    /// application does. Waiting on the figure itself is also what stops a test
    /// passing against a half-filled read model.
    /// </summary>
    private async Task<WeeklyGoalView> WaitForAsync(AuthTokens tokens, Func<WeeklyGoalView, bool> until)
    {
        await Poll.UntilAsync(
            async () => until(await ReadAsync(tokens)),
            "the goal's progress should catch up with what the account recorded",
            Ct);

        return await ReadAsync(tokens);
    }
}
