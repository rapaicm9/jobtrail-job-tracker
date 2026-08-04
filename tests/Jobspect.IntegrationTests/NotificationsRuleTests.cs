using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.SetReminderRule;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The follow-up rule over HTTP: the one thing in this module an account states
/// rather than the module deriving it.
/// <para>
/// It is a singleton rather than a collection, so there is no id in any route and no
/// error for asking for a second - the shape of the request makes the cap of one
/// unaskable. What the gate covers is the whole point of the suite: setting the
/// automation up is Pro, and reading it back and being rid of it are not.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsRuleTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    // ------------------------------------------------------------------- the rule

    [Fact]
    public async Task Setting_a_rule_records_it_and_reads_it_back()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);

        var set = await (await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 10 }))
            .ReadReminderRuleAsync();

        set.DaysAfterApplied.ShouldBe(10);

        var read = await (await _client.GetReminderRuleAsync(tokens.AccessToken)).ReadReminderRuleAsync();

        read.Id.ShouldBe(set.Id);
        read.DaysAfterApplied.ShouldBe(10);
    }

    /// <summary>
    /// A second PUT changes the rule rather than adding one, which is the cap being
    /// structural rather than enforced: the id survives, so a follow-up already
    /// pointing at the rule still points at it.
    /// </summary>
    [Fact]
    public async Task Setting_it_again_changes_the_same_rule()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);

        var first = await (await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 7 }))
            .ReadReminderRuleAsync();

        var second = await (await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 21 }))
            .ReadReminderRuleAsync();

        second.Id.ShouldBe(first.Id);
        second.DaysAfterApplied.ShouldBe(21);

        // Still says when the account first automated anything, which is what the
        // column is for - the upsert leaves it to the default on insert and does not
        // touch it on update.
        second.CreatedAt.ShouldBe(first.CreatedAt);
        second.UpdatedAt.ShouldBeGreaterThanOrEqualTo(first.UpdatedAt);

        (await RuleCountAsync(UserId.From(tokens.UserId))).ShouldBe(1);
    }

    [Fact]
    public async Task An_account_without_a_rule_has_none_to_read()
    {
        var tokens = await _client.RegisterNewUserAsync();

        (await _client.GetReminderRuleAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_accounts_rule_is_never_read()
    {
        var mine = await _client.RegisterNewUserAsync();
        var theirs = await fixture.RegisterProUserAsync(_client, Ct);

        await _client.SetReminderRuleAsync(theirs.AccessToken, new { daysAfterApplied = 14 });

        (await _client.GetReminderRuleAsync(mine.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ the gate

    [Fact]
    public async Task Free_may_not_set_one_up()
    {
        var tokens = await _client.RegisterNewUserAsync();

        // Waiting for the plan means the 403 is the entitlement being absent rather
        // than the plan not existing yet - otherwise this would pass for the wrong
        // reason.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(UserId.From(tokens.UserId), Ct) is not null,
            "registration should provision the Free plan the gate reads",
            Ct);

        (await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 7 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_is_challenged_rather_than_forbidden()
    {
        // 401, not 403: the question is "which user" before "may that user", so a
        // caller who has not said who they are is asked, not refused.
        (await _client.SetReminderRuleAsync(accessToken: null, new { daysAfterApplied = 7 }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The route that would be easiest to get wrong. A downgraded account keeps the
    /// automation it configured, has to be able to see what it is, and above all has
    /// to be able to switch it off - gating the delete would leave it holding a rule
    /// still raising nudges it had no way to stop.
    /// </summary>
    [Fact]
    public async Task A_downgraded_account_can_still_see_its_rule_and_be_rid_of_it()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 30 });
        await fixture.SetTierToFreeAsync(ownerId, Ct);

        var read = await (await _client.GetReminderRuleAsync(tokens.AccessToken)).ReadReminderRuleAsync();
        read.DaysAfterApplied.ShouldBe(30);

        (await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 14 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await _client.DeleteReminderRuleAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RuleCountAsync(ownerId)).ShouldBe(0);
    }

    // ------------------------------------------------------------------ validation

    [Theory]
    [InlineData(0)]
    [InlineData(SetReminderRuleRequestValidator.MaxDaysAfterApplied + 1)]
    [InlineData(-1)]
    public async Task A_number_of_days_outside_the_bounds_is_refused(int days)
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);

        var response = await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = days });

        await response.ShouldBeValidationProblemAsync("daysAfterApplied");
    }

    /// <summary>
    /// Absent is a failure rather than the default, because this one request both
    /// creates the rule and changes it: an omission that meant seven on the way in
    /// would mean an account's thirty days quietly reset on the way past.
    /// </summary>
    [Fact]
    public async Task An_omitted_number_of_days_is_refused_rather_than_defaulted()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);

        var response = await _client.SetReminderRuleAsync(tokens.AccessToken, new { });

        await response.ShouldBeValidationProblemAsync("daysAfterApplied");
    }

    // ------------------------------------------------------------------- deleting

    [Fact]
    public async Task Deleting_a_rule_that_is_not_there_is_success()
    {
        var tokens = await _client.RegisterNewUserAsync();

        // The caller asked to be left without one and they are. A client retrying a
        // request whose response it never saw must not be told off for it.
        (await _client.DeleteReminderRuleAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.DeleteReminderRuleAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// <b>Turning the automation off takes back what it has not yet said.</b> A
    /// pending follow-up is a nudge still sitting in the schedule, and firing it
    /// tomorrow would be the automation outliving the decision to end it. What has
    /// already reached the feed stays: that is a record of something the owner was
    /// told, and this module does not rewrite those.
    /// </summary>
    [Fact]
    public async Task Deleting_the_rule_retracts_the_follow_ups_it_has_not_delivered()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        var rule = await (await _client.SetReminderRuleAsync(tokens.AccessToken, new { daysAfterApplied = 7 }))
            .ReadReminderRuleAsync();

        var pending = await SeedFollowUpAsync(ownerId, rule.Id, ReminderState.Pending);
        var sent = await SeedFollowUpAsync(ownerId, rule.Id, ReminderState.Sent);

        (await _client.DeleteReminderRuleAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReminderAsync(pending)).State.ShouldBe(ReminderState.Cancelled);

        var delivered = await ReminderAsync(sent);
        delivered.State.ShouldBe(ReminderState.Sent);

        // The foreign key nulls out rather than cascading, so the entry the owner
        // already saw is still there and still reads the same.
        delivered.RuleId.ShouldBeNull();
    }

    // -------------------------------------------------------------------- reading

    private async Task<int> RuleCountAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.ReminderRules.CountAsync(rule => rule.OwnerId == ownerId, Ct);
    }

    private async Task<Reminder> ReminderAsync(Guid id)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders.AsNoTracking().SingleAsync(reminder => reminder.Id == id, Ct);
    }

    /// <summary>
    /// A follow-up as the scan would have left one, written directly. What is under
    /// test here is what deleting the rule does to it, and driving the scan to
    /// produce one would make this a test of two things.
    /// <para>
    /// Due far enough ahead that no sweep on the real clock can reach it while the
    /// test runs - the suite's other classes work in 2020, deliberately behind.
    /// </para>
    /// </summary>
    private async Task<Guid> SeedFollowUpAsync(UserId ownerId, Guid ruleId, ReminderState state)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var reminder = new Reminder
        {
            OwnerId = ownerId,
            Kind = ReminderKind.FollowUp,
            State = state,
            DueAt = new DateTimeOffset(2031, 3, 1, 11, 0, 0, TimeSpan.Zero),
            ApplicationId = Guid.CreateVersion7(),
            RuleId = ruleId,
            SubjectDate = new DateOnly(2031, 2, 22),
            SourceRecordedAt = new DateTimeOffset(2031, 3, 1, 9, 0, 0, TimeSpan.Zero),
        };

        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync(Ct);

        return reminder.Id;
    }
}
