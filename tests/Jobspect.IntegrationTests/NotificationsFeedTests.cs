using System.Net;
using System.Net.Http.Json;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features;
using Jobspect.Modules.Notifications.Features.SweepReminders;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The feed, over HTTP - the first thing in this module a person can actually read.
/// <para>
/// Reminders are seeded straight into the table rather than armed through the event
/// path, because what is under test is which rows become visible and in what order,
/// and a state like <c>Dropped</c> cannot be reached by any sequence of requests. The
/// arming path has its own suite; one test at the foot carries a reminder all the way
/// from a sweep into the feed.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsFeedTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The instant the seeded rows are due at, and the clock the one sweeping test
    /// reads. Deliberately behind the whole suite, for the reason
    /// <see cref="NotificationsSweepTests"/> gives: a sweep reads the entire table, so
    /// a pass at a clock ahead of the suite would deliver and drop rows other classes
    /// are still asserting about.
    /// </summary>
    private static readonly DateTimeOffset Due = new(2020, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = fixture.CreateClient();

    // ------------------------------------------------------------------ what shows

    /// <summary>
    /// The whole state model in one assertion. Three of the five states are things
    /// that were never said to anybody: <c>Pending</c> has not happened yet,
    /// <c>Cancelled</c> was retracted before it could, and <c>Dropped</c> was owed and
    /// deliberately withheld as too late - showing that one would be exactly the noise
    /// the lateness rule exists to prevent.
    /// </summary>
    [Fact]
    public async Task Only_the_states_the_owner_was_actually_told_appear()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var owner = UserId.From(tokens.UserId);

        var sent = await SeedAsync(owner, ReminderState.Sent, Due);
        var dismissed = await SeedAsync(owner, ReminderState.Dismissed, Due.AddMinutes(-1));
        await SeedAsync(owner, ReminderState.Pending, Due.AddMinutes(-2));
        await SeedAsync(owner, ReminderState.Cancelled, Due.AddMinutes(-3));
        await SeedAsync(owner, ReminderState.Dropped, Due.AddMinutes(-4));

        var feed = await ReadFeedAsync(tokens.AccessToken);

        feed.Select(entry => entry.Id).ShouldBe([sent, dismissed]);
        feed.Single(entry => entry.Id == sent).Dismissed.ShouldBeFalse();
        feed.Single(entry => entry.Id == dismissed).Dismissed.ShouldBeTrue();
    }

    [Fact]
    public async Task An_entry_carries_what_the_reminder_is_about()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var owner = UserId.From(tokens.UserId);

        var applicationId = Guid.CreateVersion7();
        var interviewId = Guid.CreateVersion7();
        var subjectAt = new DateTimeOffset(2020, 6, 2, 8, 30, 0, TimeSpan.Zero);

        await SeedAsync(
            owner,
            ReminderState.Sent,
            Due,
            applicationId,
            interviewId,
            ReminderKind.InterviewHourBefore,
            subjectAt);

        var entry = (await ReadFeedAsync(tokens.AccessToken)).ShouldHaveSingleItem();

        entry.Kind.ShouldBe(nameof(ReminderKind.InterviewHourBefore));
        entry.DueAt.ShouldBe(Due);
        entry.ApplicationId.ShouldBe(applicationId);
        entry.InterviewId.ShouldBe(interviewId);

        // The reason this column is carried on the row at all: without it the feed
        // could say a reminder is about an interview but not when the interview is.
        entry.SubjectAt.ShouldBe(subjectAt);
    }

    [Fact]
    public async Task Another_accounts_reminders_are_never_read()
    {
        var mine = await _client.RegisterNewUserAsync();
        var theirs = await _client.RegisterNewUserAsync();

        await SeedAsync(UserId.From(theirs.UserId), ReminderState.Sent, Due);

        (await ReadFeedAsync(mine.AccessToken)).ShouldBeEmpty();
        (await ReadUnreadCountAsync(mine.AccessToken)).ShouldBe(0);
    }

    // --------------------------------------------------------------------- paging

    [Fact]
    public async Task The_feed_is_newest_first_and_pages_through_the_whole_of_it()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var owner = UserId.From(tokens.UserId);

        // Seeded oldest-first, so the expected reading is the reverse of this.
        var seeded = new List<Guid>();
        for (var minute = 0; minute < 7; minute++)
        {
            seeded.Add(await SeedAsync(owner, ReminderState.Sent, Due.AddMinutes(minute)));
        }

        seeded.Reverse();

        var walked = new List<Guid>();
        string? cursor = null;

        do
        {
            var page = await (await _client.ListRemindersAsync(tokens.AccessToken, limit: 3, cursor))
                .ReadPageAsync<ReminderView>();

            walked.AddRange(page.Items.Select(entry => entry.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        // Complete, in order, and with nothing repeated across the page boundaries.
        walked.ShouldBe(seeded);
    }

    /// <summary>
    /// The gotcha the cursor codec was written around: a non-base64 character makes
    /// the framework's decode throw rather than return false, so a garbage cursor is a
    /// 500 unless it is checked first.
    /// </summary>
    [Fact]
    public async Task A_cursor_that_is_not_one_is_refused_rather_than_crashing()
    {
        var tokens = await _client.RegisterNewUserAsync();

        var response = await _client.ListRemindersAsync(tokens.AccessToken, cursor: "not-a-cursor!!");

        await response.ShouldBeValidationProblemAsync("cursor");
    }

    [Fact]
    public async Task A_limit_outside_the_bounds_is_refused_rather_than_clamped()
    {
        var tokens = await _client.RegisterNewUserAsync();

        var response = await _client.ListRemindersAsync(tokens.AccessToken, limit: PagingParameters.MaxLimit + 1);

        await response.ShouldBeValidationProblemAsync("limit");
    }

    /// <summary>
    /// This module keeps its own copy of the paging bounds, as it does its own
    /// <c>Problems</c> and <c>Caller</c>. The copy is deliberate; drifting from the
    /// other module's is not, and a client that learns the ceiling from one list and
    /// is refused at it by another has no way to tell which is right.
    /// </summary>
    [Fact]
    public void The_paging_bounds_match_the_other_modules()
    {
        PagingParameters.DefaultLimit.ShouldBe(
            Jobspect.Modules.Applications.Features.PagingParameters.DefaultLimit);
        PagingParameters.MaxLimit.ShouldBe(
            Jobspect.Modules.Applications.Features.PagingParameters.MaxLimit);
    }

    // ------------------------------------------------------------------- dismissal

    [Fact]
    public async Task Dismissing_clears_an_entry_and_drops_the_unread_count()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var owner = UserId.From(tokens.UserId);

        var first = await SeedAsync(owner, ReminderState.Sent, Due);
        await SeedAsync(owner, ReminderState.Sent, Due.AddMinutes(-1));

        (await ReadUnreadCountAsync(tokens.AccessToken)).ShouldBe(2);

        var dismissed = await DismissAsync(tokens.AccessToken, first);

        dismissed.Id.ShouldBe(first);
        dismissed.Dismissed.ShouldBeTrue();

        (await ReadUnreadCountAsync(tokens.AccessToken)).ShouldBe(1);

        // Still in the feed - dismissing clears the badge, not the history.
        (await ReadFeedAsync(tokens.AccessToken)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Dismissing_twice_is_not_an_error()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var id = await SeedAsync(UserId.From(tokens.UserId), ReminderState.Sent, Due);

        (await _client.DismissReminderAsync(tokens.AccessToken, id)).IsSuccessStatusCode.ShouldBeTrue();

        // A client retrying a request whose response it never saw must not be told the
        // reminder has vanished.
        (await DismissAsync(tokens.AccessToken, id)).Dismissed.ShouldBeTrue();
        (await ReadUnreadCountAsync(tokens.AccessToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_reminder_that_was_never_delivered_has_no_entry_to_dismiss()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var owner = UserId.From(tokens.UserId);

        var pending = await SeedAsync(owner, ReminderState.Pending, Due);
        var dropped = await SeedAsync(owner, ReminderState.Dropped, Due.AddMinutes(-1));

        (await _client.DismissReminderAsync(tokens.AccessToken, pending))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.DismissReminderAsync(tokens.AccessToken, dropped))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_accounts_reminder_is_a_404_rather_than_a_403()
    {
        var mine = await _client.RegisterNewUserAsync();
        var theirs = await _client.RegisterNewUserAsync();

        var id = await SeedAsync(UserId.From(theirs.UserId), ReminderState.Sent, Due);

        // Not 403: telling the caller it exists is already more than they should learn.
        (await _client.DismissReminderAsync(mine.AccessToken, id))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_feed_needs_a_token()
    {
        (await _client.ListRemindersAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetUnreadReminderCountAsync(accessToken: null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------- sweep to reader

    /// <summary>
    /// The whole engine, as far as one process can show it: a reminder that has come
    /// due is swept, and what the sweep did is then read back over HTTP. Everything
    /// before this box proved delivery by looking at a table.
    /// </summary>
    [Fact]
    public async Task A_swept_reminder_arrives_in_the_feed_unread()
    {
        var tokens = await _client.RegisterNewUserAsync();
        var id = await SeedAsync(UserId.From(tokens.UserId), ReminderState.Pending, Due);

        (await ReadFeedAsync(tokens.AccessToken)).ShouldBeEmpty();

        await SweepAsync();

        var entry = (await ReadFeedAsync(tokens.AccessToken)).ShouldHaveSingleItem();
        entry.Id.ShouldBe(id);
        entry.Dismissed.ShouldBeFalse();

        (await ReadUnreadCountAsync(tokens.AccessToken)).ShouldBe(1);
    }

    // --------------------------------------------------------------------- driving

    private async Task SweepAsync()
    {
        using var scope = fixture.CreateScope();
        var provider = scope.ServiceProvider;

        await new ReminderSweep(
                provider.GetRequiredService<NotificationsDbContext>(),
                new FixedClock(Due),
                provider.GetRequiredService<ILogger<ReminderSweep>>())
            .SweepAsync(Ct);
    }

    private async Task<Guid> SeedAsync(
        UserId ownerId,
        ReminderState state,
        DateTimeOffset dueAt,
        Guid? applicationId = null,
        Guid? interviewId = null,
        ReminderKind kind = ReminderKind.ApplicationDeadlineMorningOf,
        DateTimeOffset? subjectAt = null)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var reminder = new Reminder
        {
            OwnerId = ownerId,
            Kind = kind,
            State = state,
            DueAt = dueAt,
            ApplicationId = applicationId ?? Guid.CreateVersion7(),
            InterviewId = interviewId,
            SubjectAt = subjectAt,
            SourceRecordedAt = dueAt.AddDays(-1),
        };

        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync(Ct);

        return reminder.Id;
    }

    // --------------------------------------------------------------------- reading

    private async Task<IReadOnlyList<ReminderView>> ReadFeedAsync(string? accessToken) =>
        (await (await _client.ListRemindersAsync(accessToken)).ReadPageAsync<ReminderView>()).Items;

    private async Task<ReminderView> DismissAsync(string? accessToken, Guid id)
    {
        var response = await _client.DismissReminderAsync(accessToken, id);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"expected a success status but got {(int)response.StatusCode}");

        return (await response.Content.ReadFromJsonAsync<ReminderView>(Ct)).ShouldNotBeNull();
    }

    private async Task<int> ReadUnreadCountAsync(string? accessToken)
    {
        var response = await _client.GetUnreadReminderCountAsync(accessToken);
        response.IsSuccessStatusCode.ShouldBeTrue();

        var count = await response.Content.ReadFromJsonAsync<UnreadCountView>(Ct);
        return count.ShouldNotBeNull().Count;
    }
}
