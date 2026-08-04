using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.ScanFollowUps;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The follow-up scan's rules, driven directly on a day of this suite's choosing.
/// <para>
/// The scan runs on a Quartz trigger in the worker, which is precisely why nothing
/// here goes through one: every rule below is a statement about how long is too long,
/// and a test that could not decide what day it was would be asserting against the
/// calendar. That the trigger exists and reaches the work is proven once, on the real
/// clock, in <see cref="NotificationsSchedulerTests"/>.
/// </para>
/// <para>
/// Each test owns a fresh account, and every assertion is about that account's rows.
/// <b>Nothing here asserts how many follow-ups a pass raised</b>, deliberately: a scan
/// is global, like the sweep beside it, so its count includes whatever the rest of
/// this class has left waiting for the day it is scanning. The rows are the isolated
/// fact; the number is not.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsScanTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The moment every scan here runs at. Deliberately years behind the real clock,
    /// for the reason <see cref="NotificationsSweepTests"/> gives about its own: what
    /// this raises is <c>Pending</c>, and a sweep running on the real clock must find
    /// none of it due.
    /// <para>
    /// 06:00 UTC is 08:00 in the zone below, so the local 11:00 is still ahead and
    /// "the next morning" is today's - which is what makes the one test that scans
    /// exactly at 11:00 local a different case rather than the same one.
    /// </para>
    /// </summary>
    private static readonly DateTimeOffset Now = new(2020, 6, 10, 6, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One zone for most of these, and not UTC: a rule counted in UTC days would pass
    /// every test written against a UTC clock and still fire a day early for everyone
    /// east of it.
    /// </summary>
    private const string Zone = "Europe/Belgrade";

    // ------------------------------------------------------------- what is raised

    [Fact]
    public async Task An_application_silent_for_long_enough_gets_a_follow_up()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 3));

        await ScanAsync();

        var followUp = await FollowUpAsync(applicationId);
        followUp.ShouldNotBeNull();
        followUp.OwnerId.ShouldBe(owner);
        followUp.State.ShouldBe(ReminderState.Pending);

        // What the reminder is about is the day the wait started, which is the only
        // date a follow-up has - there is no moment it concerns.
        followUp.SubjectDate.ShouldBe(new DateOnly(2020, 6, 3));
        followUp.SubjectAt.ShouldBeNull();
        followUp.InterviewId.ShouldBeNull();

        // And the automation that raised it, which is the one real foreign key on
        // this table.
        followUp.RuleId.ShouldNotBeNull();
    }

    /// <summary>
    /// A follow-up fires at 11:00 in the owner's own timezone, like every other
    /// clock-based reminder here - which is the whole reason the worker composes
    /// Identity's profile read at all.
    /// </summary>
    [Fact]
    public async Task It_is_due_at_the_next_local_morning()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 3));

        await ScanAsync();

        // 11:00 in Belgrade on the day of the scan, which is 09:00 UTC in summer.
        (await FollowUpAsync(applicationId))!.DueAt.ShouldBe(
            new DateTimeOffset(2020, 6, 10, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// The instant is strictly after the scan's reading, so the sweep can never find
    /// a follow-up that was due the moment it was raised. Scanning at exactly the
    /// local morning takes tomorrow's.
    /// </summary>
    [Fact]
    public async Task A_scan_at_the_local_morning_raises_it_for_the_next_one()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 3));

        // 11:00 in Belgrade, exactly.
        await ScanAsync(at: new DateTimeOffset(2020, 6, 10, 9, 0, 0, TimeSpan.Zero));

        (await FollowUpAsync(applicationId))!.DueAt.ShouldBe(
            new DateTimeOffset(2020, 6, 11, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Most applications never move, so the row that records one carries no stage at
    /// all - only the submission creates it, and that event does not say where an
    /// application starts. A null stage and a recorded "Applied" mean the same thing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Applied")]
    public async Task Waiting_is_a_null_stage_or_an_applied_one(string? stage)
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, new DateOnly(2020, 6, 1), stage);

        await ScanAsync();

        (await FollowUpAsync(applicationId)).ShouldNotBeNull();
    }

    // --------------------------------------------------------- what is left alone

    [Fact]
    public async Task An_application_that_has_moved_is_not_nudged()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, new DateOnly(2020, 6, 1), stage: "Interview");

        await ScanAsync();

        (await FollowUpAsync(applicationId)).ShouldBeNull();
    }

    [Fact]
    public async Task An_application_that_has_not_waited_long_enough_is_not_nudged()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);

        // Six days before the scan's local today, so the seventh has not arrived.
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 4));

        await ScanAsync();

        (await FollowUpAsync(applicationId)).ShouldBeNull();
    }

    /// <summary>
    /// The boundary itself, because "N days" is a rule and a rule is worth pinning at
    /// its edge: the day it becomes true, the nudge is raised.
    /// </summary>
    [Fact]
    public async Task The_day_the_wait_reaches_the_rule_is_the_day_it_is_nudged()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var onTheDay = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 3));
        var aDayShort = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 4));

        await ScanAsync();

        (await FollowUpAsync(onTheDay)).ShouldNotBeNull();
        (await FollowUpAsync(aDayShort)).ShouldBeNull();
    }

    [Fact]
    public async Task An_account_without_a_rule_is_never_scanned()
    {
        var owner = UserId.New();
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 1, 1));

        await ScanAsync();

        (await FollowUpAsync(applicationId)).ShouldBeNull();
    }

    /// <summary>
    /// An application recorded here before its submission arrived has no date to
    /// count from - only <c>ApplicationSubmitted</c> carries one, and a stage change
    /// delivered first creates the row without it. Nudging on a wait of unknown
    /// length is not something to guess at.
    /// </summary>
    [Fact]
    public async Task An_application_with_no_applied_date_yet_is_not_nudged()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, appliedDate: null);

        await ScanAsync();

        (await FollowUpAsync(applicationId)).ShouldBeNull();
    }

    // ---------------------------------------------------------------- the exclusion

    /// <summary>
    /// <b>The trap this whole feature turns on, in all five states.</b> The unique
    /// index that stops a slot being armed twice is partial on <c>Pending</c>, so a
    /// follow-up in any other state leaves the slot free - and an exclusion that only
    /// looked at armed rows would raise the same nudge on every pass for the rest of
    /// the application's life. <c>Cancelled</c> is the one that matters most: a move
    /// retracts the follow-up, so it is the state a reopened application's sits in.
    /// </summary>
    /// <remarks>
    /// The state travels as its name because the enum is internal to the module and
    /// a test method taking one could not be public.
    /// </remarks>
    [Theory]
    [InlineData(nameof(ReminderState.Pending))]
    [InlineData(nameof(ReminderState.Sent))]
    [InlineData(nameof(ReminderState.Dismissed))]
    [InlineData(nameof(ReminderState.Cancelled))]
    [InlineData(nameof(ReminderState.Dropped))]
    public async Task An_application_that_has_had_a_follow_up_in_any_state_never_gets_another(string state)
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 1));

        await SeedFollowUpAsync(owner, applicationId, Enum.Parse<ReminderState>(state));

        await ScanAsync();

        (await FollowUpCountAsync(applicationId)).ShouldBe(1);
    }

    /// <summary>
    /// The same rule from the other side, and the property that makes an hourly job
    /// affordable: a pass raises what is owed, and every later pass finds nothing,
    /// because what it raised excludes itself.
    /// </summary>
    [Fact]
    public async Task Scanning_again_raises_nothing()
    {
        var owner = await AnAccountWithARuleAsync(days: 7);
        var applicationId = await TrackedAsync(owner, appliedDate: new DateOnly(2020, 6, 1));

        await ScanAsync();
        (await FollowUpCountAsync(applicationId)).ShouldBe(1);

        await ScanAsync();
        (await FollowUpCountAsync(applicationId)).ShouldBe(1);

        // And still nothing a month later, which is the case that would fail loudest
        // if the exclusion looked only at armed rows: by then the nudge has been
        // swept, and its slot is free.
        await ScanAsync(at: Now.AddDays(30));
        (await FollowUpCountAsync(applicationId)).ShouldBe(1);
    }

    // ------------------------------------------------------------------- seeding

    private async Task<UserId> AnAccountWithARuleAsync(int days)
    {
        var ownerId = UserId.New();

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        dbContext.ReminderRules.Add(new ReminderRule
        {
            OwnerId = ownerId,
            DaysAfterApplied = days,
            UpdatedAt = Now,
        });

        await dbContext.SaveChangesAsync(Ct);

        return ownerId;
    }

    /// <summary>
    /// An application as the trackers would have recorded one. Written directly
    /// because what is under test is what the scan makes of the record, and the two
    /// events that fill it have a suite of their own.
    /// </summary>
    private async Task<Guid> TrackedAsync(UserId ownerId, DateOnly? appliedDate, string? stage = null)
    {
        var applicationId = Guid.CreateVersion7();

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        dbContext.TrackedApplications.Add(new TrackedApplication
        {
            ApplicationId = applicationId,
            OwnerId = ownerId,
            AppliedDate = appliedDate,
            Stage = stage,
            StageRecordedAt = stage is null ? null : Now,
        });

        await dbContext.SaveChangesAsync(Ct);

        return applicationId;
    }

    private async Task SeedFollowUpAsync(UserId ownerId, Guid applicationId, ReminderState state)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        dbContext.Reminders.Add(new Reminder
        {
            OwnerId = ownerId,
            Kind = ReminderKind.FollowUp,
            State = state,
            DueAt = Now.AddDays(1),
            ApplicationId = applicationId,
            SubjectDate = new DateOnly(2020, 6, 1),
            SourceRecordedAt = Now,
        });

        await dbContext.SaveChangesAsync(Ct);
    }

    // ------------------------------------------------------------------ scanning

    /// <summary>
    /// The scan as the worker composes it, minus the two things a test has to decide
    /// for itself: what time it is, and where the owner lives. The timezone is stubbed
    /// rather than read from a registered account, because these accounts have no
    /// Identity row - what the real profile read does with a real one is the concern
    /// of the tests that go through the host.
    /// </summary>
    private async Task<int> ScanAsync(DateTimeOffset? at = null, string? zone = Zone)
    {
        using var scope = fixture.CreateScope();
        var provider = scope.ServiceProvider;

        var scan = new ReminderScan(
            provider.GetRequiredService<NotificationsDbContext>(),
            new StubProfileQuery(zone),
            new FixedClock(at ?? Now),
            provider.GetRequiredService<ILogger<ReminderScan>>());

        return await scan.ScanAsync(Ct);
    }

    // ------------------------------------------------------------------- reading

    private async Task<Reminder?> FollowUpAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reminder => reminder.ApplicationId == applicationId && reminder.Kind == ReminderKind.FollowUp,
                Ct);
    }

    private async Task<int> FollowUpCountAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders.CountAsync(
            reminder => reminder.ApplicationId == applicationId && reminder.Kind == ReminderKind.FollowUp,
            Ct);
    }
}
