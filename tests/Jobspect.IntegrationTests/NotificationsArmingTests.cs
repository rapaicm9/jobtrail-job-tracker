using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Reminders being armed from the events, in two layers.
/// <para>
/// The first is the wiring: a real request over HTTP, and the rows appearing behind
/// it through the real outbox. The second is the arming rules, driven by handing
/// the handlers events directly - because the cases worth proving are redelivery,
/// out-of-order arrival and a clock reading of the test's choosing, and a live
/// dispatcher can be asked for none of them. Both run against the real database;
/// the guards are SQL, so there is nothing here a fake could answer for.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsArmingTests(ApiFixture fixture)
{
    private const string Belgrade = "Europe/Belgrade";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Whole seconds, so nothing here depends on how PostgreSQL rounds.
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = fixture.CreateClient();

    // ---------------------------------------------------------------- wiring

    [Fact]
    public async Task A_scheduled_interview_reaches_the_reminder_table()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct, timeZoneId: Belgrade);

        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Platform Engineer" })).ReadApplicationAsync();

        // Far enough ahead that both instants are comfortably in the future
        // whenever this suite happens to run.
        var scheduledAt = DateTimeOffset.UtcNow.AddDays(30);

        (await _client.CreateInterviewAsync(tokens.AccessToken, created.Id, new
        {
            scheduledAt,
            type = "Technical",
            format = "Remote",
        })).IsSuccessStatusCode.ShouldBeTrue();

        var armed = await PollForArmedAsync(created.Id, expected: 2);

        armed.Select(reminder => reminder.Kind).ShouldBe(
            [ReminderKind.InterviewMorningBefore, ReminderKind.InterviewHourBefore],
            ignoreOrder: true);

        armed.ShouldAllBe(reminder => reminder.OwnerId == UserId.From(tokens.UserId));

        // The round's own instant travels onto the row: nothing can recover it
        // later, and without it the feed cannot say when the interview is.
        armed.ShouldAllBe(reminder => reminder.SubjectAt != null);
        armed.ShouldAllBe(reminder => reminder.InterviewId != null);
    }

    [Fact]
    public async Task A_recorded_deadline_reaches_the_reminder_table()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct, timeZoneId: Belgrade);

        var created = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Platform Engineer",
            applicationDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        })).ReadApplicationAsync();

        var armed = await PollForArmedAsync(created.Id, expected: 2);

        armed.Select(reminder => reminder.Kind).ShouldBe(
            [
                ReminderKind.ApplicationDeadlineThreeDaysBefore,
                ReminderKind.ApplicationDeadlineMorningOf,
            ],
            ignoreOrder: true);

        // A deadline is a day, not a moment - it rides as a date.
        armed.ShouldAllBe(reminder => reminder.SubjectDate != null && reminder.SubjectAt == null);
        armed.ShouldAllBe(reminder => reminder.InterviewId == null);
    }

    // ----------------------------------------------------------- arming rules

    [Fact]
    public async Task Rescheduling_a_round_retires_its_instants_and_arms_new_ones()
    {
        var (applicationId, interviewId, ownerId) = NewSubject();

        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddDays(30), T1);
        var first = await ArmedIdsAsync(applicationId);

        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddDays(40), T2);

        var armed = await RemindersAsync(applicationId, ReminderState.Pending);
        var cancelled = await RemindersAsync(applicationId, ReminderState.Cancelled);

        // Two armed instants, and the two they replaced kept as history rather
        // than rewritten - the feed is a record of what the owner was told.
        armed.Count.ShouldBe(2);
        cancelled.Count.ShouldBe(2);
        cancelled.Select(reminder => reminder.Id).ShouldBe(first, ignoreOrder: true);
        armed.Select(reminder => reminder.Id).ShouldNotBeOneOf([.. first]);
    }

    [Fact]
    public async Task An_exact_redelivery_changes_nothing()
    {
        var (applicationId, interviewId, ownerId) = NewSubject();
        var scheduledAt = Now.AddDays(30);

        await ArmInterviewAsync(applicationId, interviewId, ownerId, scheduledAt, T1);
        var first = await ArmedIdsAsync(applicationId);

        // At-least-once delivery makes this the ordinary case, not a rare one.
        await ArmInterviewAsync(applicationId, interviewId, ownerId, scheduledAt, T1);

        // Same rows, same ids: no churn, and nothing retired and re-armed behind
        // the owner's back.
        (await ArmedIdsAsync(applicationId)).ShouldBe(first, ignoreOrder: true);
        (await RemindersAsync(applicationId, ReminderState.Cancelled)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_older_event_does_not_disturb_what_a_newer_one_decided()
    {
        var (applicationId, interviewId, ownerId) = NewSubject();

        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddDays(40), T2);
        var newest = await ArmedIdsAsync(applicationId);

        // The same round as it was announced the first time, arriving late.
        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddDays(30), T1);

        (await ArmedIdsAsync(applicationId)).ShouldBe(newest, ignoreOrder: true);
        (await RemindersAsync(applicationId, ReminderState.Cancelled)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cleared_deadline_retracts_what_it_armed()
    {
        var (applicationId, _, ownerId) = NewSubject();
        var deadline = DateOnly.FromDateTime(Now.AddDays(30).UtcDateTime);

        await ArmDeadlineAsync(applicationId, ownerId, deadline, T1);
        (await RemindersAsync(applicationId, ReminderState.Pending)).Count.ShouldBe(2);

        // A null deadline is a statement, not an omission.
        await ArmDeadlineAsync(applicationId, ownerId, deadline: null, T2);

        (await RemindersAsync(applicationId, ReminderState.Pending)).ShouldBeEmpty();
        (await RemindersAsync(applicationId, ReminderState.Cancelled)).Count.ShouldBe(2);
    }

    /// <summary>
    /// The reason the staleness guard reads the newest row for a slot in <em>any</em>
    /// state. Comparing against armed rows alone, this redelivery would find an
    /// empty slot and raise reminders for a deadline that was deleted.
    /// </summary>
    [Fact]
    public async Task A_redelivered_arming_cannot_revive_a_retracted_slot()
    {
        var (applicationId, _, ownerId) = NewSubject();
        var deadline = DateOnly.FromDateTime(Now.AddDays(30).UtcDateTime);

        await ArmDeadlineAsync(applicationId, ownerId, deadline, T1);
        await ArmDeadlineAsync(applicationId, ownerId, deadline: null, T2);

        // The original arming, delivered again after the clearance that retired it.
        await ArmDeadlineAsync(applicationId, ownerId, deadline, T1);

        (await RemindersAsync(applicationId, ReminderState.Pending)).ShouldBeEmpty();
    }

    /// <summary>
    /// A round moved close enough that its morning has already gone by. The instant
    /// is not armed again - and the row still holding the old morning must not be
    /// left to fire for a round that has moved.
    /// </summary>
    [Fact]
    public async Task A_round_moved_inside_its_own_lead_retires_the_morning_it_can_no_longer_announce()
    {
        var (applicationId, interviewId, ownerId) = NewSubject();

        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddDays(30), T1);
        (await RemindersAsync(applicationId, ReminderState.Pending)).Count.ShouldBe(2);

        // Ninety minutes out: the hour-before is still ahead, the morning is not.
        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddMinutes(90), T2);

        var armed = await RemindersAsync(applicationId, ReminderState.Pending);

        armed.Select(reminder => reminder.Kind).ShouldBe([ReminderKind.InterviewHourBefore]);
        (await RemindersAsync(applicationId, ReminderState.Cancelled)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_round_already_upon_us_arms_nothing_and_retires_everything()
    {
        var (applicationId, interviewId, ownerId) = NewSubject();

        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddDays(30), T1);
        await ArmInterviewAsync(applicationId, interviewId, ownerId, Now.AddMinutes(10), T2);

        (await RemindersAsync(applicationId, ReminderState.Pending)).ShouldBeEmpty();
    }

    /// <summary>
    /// However often a round moves, the slot holds one armed instant. This is the
    /// partial unique index doing its job, and it only does it because nulls are
    /// non-distinct - which is what the deadline half of this test covers, since
    /// those kinds carry no interview id at all.
    /// </summary>
    [Fact]
    public async Task However_often_a_subject_moves_each_slot_holds_one_armed_instant()
    {
        var (applicationId, interviewId, ownerId) = NewSubject();

        for (var day = 1; day <= 5; day++)
        {
            await ArmInterviewAsync(
                applicationId, interviewId, ownerId, Now.AddDays(30 + day), T1.AddDays(day));

            await ArmDeadlineAsync(
                applicationId,
                ownerId,
                DateOnly.FromDateTime(Now.AddDays(30 + day).UtcDateTime),
                T1.AddDays(day));
        }

        var armed = await RemindersAsync(applicationId, ReminderState.Pending);

        armed.Count.ShouldBe(4);
        armed.Select(reminder => reminder.Kind).Distinct().Count().ShouldBe(4);
    }

    // ----------------------------------------------------------------- driving

    private static (Guid ApplicationId, Guid InterviewId, UserId OwnerId) NewSubject() =>
        (Guid.CreateVersion7(), Guid.CreateVersion7(), UserId.New());

    private async Task ArmInterviewAsync(
        Guid applicationId, Guid interviewId, UserId ownerId, DateTimeOffset scheduledAt, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await new InterviewScheduledHandler(
                new ReminderWriter(dbContext), new StubProfileQuery(Belgrade), new FixedClock(Now))
            .HandleAsync(
                new InterviewScheduled(
                    Guid.CreateVersion7(), applicationId, interviewId, ownerId, scheduledAt, occurredAt),
                Ct);
    }

    private async Task ArmDeadlineAsync(
        Guid applicationId, UserId ownerId, DateOnly? deadline, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await new ApplicationDeadlineSetHandler(
                new ReminderWriter(dbContext), new StubProfileQuery(Belgrade), new FixedClock(Now))
            .HandleAsync(
                new ApplicationDeadlineSet(Guid.CreateVersion7(), applicationId, ownerId, deadline, occurredAt),
                Ct);
    }

    // ----------------------------------------------------------------- reading

    private async Task<IReadOnlyList<Reminder>> RemindersAsync(Guid applicationId, ReminderState state)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.ApplicationId == applicationId && reminder.State == state)
            .ToListAsync(Ct);
    }

    private async Task<IReadOnlyList<Guid>> ArmedIdsAsync(Guid applicationId) =>
        [.. (await RemindersAsync(applicationId, ReminderState.Pending)).Select(reminder => reminder.Id)];

    private async Task<IReadOnlyList<Reminder>> PollForArmedAsync(Guid applicationId, int expected)
    {
        await Poll.UntilAsync(
            async () => (await RemindersAsync(applicationId, ReminderState.Pending)).Count == expected,
            $"the outbox should deliver the event and arm {expected} reminders",
            Ct);

        return await RemindersAsync(applicationId, ReminderState.Pending);
    }
}
