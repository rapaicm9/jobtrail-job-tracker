using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.ArmReminders;
using Jobspect.Modules.Notifications.Features.RetractReminders;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// Taking reminders back, from the three events that say something has stopped being
/// worth saying.
/// <para>
/// The three are not one operation with different arguments, and both directions of
/// getting that wrong are what these tests are for: a move that retracted an interview
/// alert would silence the reminder the owner most needs, and a closing that missed one
/// would announce a round belonging to an application rejected weeks ago.
/// </para>
/// <para>
/// Driven by handing the handlers events directly, as the arming tests do. The cases
/// worth proving are two events sharing an instant, a redelivery arriving after the
/// closing that answered it, and rows in states no endpoint can produce - and a live
/// dispatcher can be asked for none of them. One test at the foot goes over HTTP for
/// the wiring.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsRetractionTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Whole seconds, so nothing here depends on how PostgreSQL rounds. Far enough
    // ahead that no sweep at a real clock could ever find these rows due.
    private static readonly DateTimeOffset Armed = new(2031, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Answered = new(2031, 6, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueAt = new(2031, 7, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Everything one application can hold at once: the follow-up, a round's pair, and
    /// a posting deadline's pair. Five slots, of which a stage change owns exactly one.
    /// </summary>
    private static readonly ReminderKind[] DeadlineKinds =
        [ReminderKind.ApplicationDeadlineThreeDaysBefore, ReminderKind.ApplicationDeadlineMorningOf];

    // ------------------------------------------------------------ what each source owns

    /// <summary>
    /// The failure this handler is most likely to be written as. A move is the answer
    /// the follow-up was waiting for and says nothing about the rest - an application
    /// advancing to Interview needs its interview alerts more, not less.
    /// </summary>
    [Fact]
    public async Task A_move_retracts_the_follow_up_and_leaves_everything_else_armed()
    {
        var subject = await FullyArmedAsync();

        await StageChangedAsync(subject.ApplicationId, Answered);

        (await KindsAsync(subject.ApplicationId, ReminderState.Cancelled))
            .ShouldBe([ReminderKind.FollowUp]);

        (await KindsAsync(subject.ApplicationId, ReminderState.Pending)).ShouldBe(
            [.. ReminderInstants.InterviewKinds, .. DeadlineKinds],
            ignoreOrder: true);
    }

    /// <summary>
    /// The reason a closing cannot be routed through the slot-scoped retraction with no
    /// round: <c>interview_id IS NOT DISTINCT FROM NULL</c> matches only the kinds that
    /// carry no round, so the two that do would be left armed - and nothing would notice
    /// until one fired for an application closed weeks earlier.
    /// </summary>
    [Fact]
    public async Task A_closing_retracts_every_kind_including_the_ones_that_carry_a_round()
    {
        var subject = await FullyArmedAsync();

        await ReachedTerminalAsync(subject.ApplicationId, Answered);

        (await RemindersAsync(subject.ApplicationId, ReminderState.Pending)).ShouldBeEmpty();
        (await RemindersAsync(subject.ApplicationId, ReminderState.Cancelled)).Count.ShouldBe(5);
    }

    [Fact]
    public async Task A_cancelled_round_leaves_another_rounds_pair_armed()
    {
        var applicationId = Guid.CreateVersion7();
        var cancelled = Guid.CreateVersion7();
        var survives = Guid.CreateVersion7();

        await SeedAsync(applicationId, ReminderInstants.InterviewKinds, cancelled);
        await SeedAsync(applicationId, ReminderInstants.InterviewKinds, survives);

        await InterviewCancelledAsync(applicationId, cancelled, Answered);

        var armed = await RemindersAsync(applicationId, ReminderState.Pending);

        armed.Count.ShouldBe(2);
        armed.ShouldAllBe(reminder => reminder.InterviewId == survives);
    }

    // ------------------------------------------------------------------- the two together

    /// <summary>
    /// A closing move publishes both events in one transaction at one instant, and they
    /// may be delivered either way round. Both retract, and that is safe rather than
    /// merely tolerated: a retraction only moves a row from armed to cancelled, and the
    /// staleness comparison is <c>&gt;=</c> precisely so the second of two events sharing
    /// an instant is not refused as stale by the first.
    /// </summary>
    [Fact]
    public async Task The_two_events_a_closing_publishes_reach_the_same_state_either_way_round()
    {
        var first = await FullyArmedAsync();
        await StageChangedAsync(first.ApplicationId, Answered);
        await ReachedTerminalAsync(first.ApplicationId, Answered);

        var second = await FullyArmedAsync();
        await ReachedTerminalAsync(second.ApplicationId, Answered);
        await StageChangedAsync(second.ApplicationId, Answered);

        (await StatesByKindAsync(first.ApplicationId))
            .ShouldBe(await StatesByKindAsync(second.ApplicationId));

        (await RemindersAsync(first.ApplicationId, ReminderState.Pending)).ShouldBeEmpty();
    }

    // -------------------------------------------------------------------- what survives

    /// <summary>
    /// The interview counterpart of the deadline case the arming tests already cover,
    /// and the one with teeth: a closed application must not get its interview alerts
    /// back because the event that armed them was delivered a second time.
    /// </summary>
    [Fact]
    public async Task A_redelivered_arming_cannot_revive_a_closed_application()
    {
        var applicationId = Guid.CreateVersion7();
        var interviewId = Guid.CreateVersion7();
        var ownerId = UserId.New();

        await ArmInterviewAsync(applicationId, interviewId, ownerId, Armed);
        (await RemindersAsync(applicationId, ReminderState.Pending)).Count.ShouldBe(2);

        await ReachedTerminalAsync(applicationId, Answered);

        // The original scheduling, arriving again after the closing that retired it.
        await ArmInterviewAsync(applicationId, interviewId, ownerId, Armed);

        (await RemindersAsync(applicationId, ReminderState.Pending)).ShouldBeEmpty();
    }

    /// <summary>
    /// A retraction takes back what was owed, never what was said. The feed is a record
    /// of things the owner was actually told, and a closing arriving afterwards does not
    /// make them untold.
    /// </summary>
    [Fact]
    public async Task A_closing_does_not_rewrite_what_the_owner_was_already_told()
    {
        var applicationId = Guid.CreateVersion7();

        await SeedAsync(applicationId, [ReminderKind.InterviewMorningBefore], Guid.CreateVersion7(),
            ReminderState.Sent);
        await SeedAsync(applicationId, [ReminderKind.ApplicationDeadlineMorningOf], interviewId: null,
            ReminderState.Dismissed);
        await SeedAsync(applicationId, [ReminderKind.FollowUp], interviewId: null);

        await ReachedTerminalAsync(applicationId, Answered);

        (await KindsAsync(applicationId, ReminderState.Sent))
            .ShouldBe([ReminderKind.InterviewMorningBefore]);
        (await KindsAsync(applicationId, ReminderState.Dismissed))
            .ShouldBe([ReminderKind.ApplicationDeadlineMorningOf]);
        (await KindsAsync(applicationId, ReminderState.Cancelled))
            .ShouldBe([ReminderKind.FollowUp]);
    }

    // ------------------------------------------------------------------------- wiring

    [Fact]
    public async Task Closing_an_application_over_http_retracts_the_reminders_it_armed()
    {
        var client = fixture.CreateClient();
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(client, Ct, timeZoneId: "Europe/Belgrade");

        var created = await (await client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Platform Engineer" })).ReadApplicationAsync();

        (await client.CreateInterviewAsync(tokens.AccessToken, created.Id, new
        {
            scheduledAt = DateTimeOffset.UtcNow.AddDays(30),
            type = "Technical",
            format = "Remote",
        })).IsSuccessStatusCode.ShouldBeTrue();

        await Poll.UntilAsync(
            async () => (await RemindersAsync(created.Id, ReminderState.Pending)).Count == 2,
            "the outbox should deliver the scheduling and arm both instants",
            Ct);

        (await client.TransitionApplicationAsync(tokens.AccessToken, created.Id, "Rejected"))
            .IsSuccessStatusCode.ShouldBeTrue();

        await Poll.UntilAsync(
            async () => (await RemindersAsync(created.Id, ReminderState.Pending)).Count == 0,
            "closing the application should retract both",
            Ct);

        (await RemindersAsync(created.Id, ReminderState.Cancelled)).Count.ShouldBe(2);
    }

    // ------------------------------------------------------------------------ driving

    private async Task StageChangedAsync(Guid applicationId, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();

        await new ApplicationStageChangedRetraction(Writer(scope)).HandleAsync(
            new ApplicationStageChanged(
                Guid.CreateVersion7(), applicationId, UserId.New(), "Applied", "Screening", occurredAt),
            Ct);
    }

    private async Task ReachedTerminalAsync(Guid applicationId, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();

        await new ApplicationReachedTerminalRetraction(Writer(scope)).HandleAsync(
            new ApplicationReachedTerminal(
                Guid.CreateVersion7(), applicationId, UserId.New(), "Applied", "Rejected", occurredAt),
            Ct);
    }

    private async Task InterviewCancelledAsync(Guid applicationId, Guid interviewId, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();

        await new InterviewCancelledRetraction(Writer(scope)).HandleAsync(
            new InterviewCancelled(
                Guid.CreateVersion7(), applicationId, interviewId, UserId.New(), occurredAt),
            Ct);
    }

    private async Task ArmInterviewAsync(
        Guid applicationId, Guid interviewId, UserId ownerId, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();

        await new InterviewScheduledHandler(
                Writer(scope), new StubProfileQuery("Europe/Belgrade"), new FixedClock(Armed))
            .HandleAsync(
                new InterviewScheduled(
                    Guid.CreateVersion7(), applicationId, interviewId, ownerId, DueAt, occurredAt),
                Ct);
    }

    private static ReminderWriter Writer(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>());

    // ------------------------------------------------------------------------ seeding

    /// <summary>
    /// One application holding every kind at once: a follow-up, a round's pair, and a
    /// posting deadline's pair. Written straight to the table rather than armed through
    /// the handlers, because nothing raises a follow-up yet - the rule and the scan that
    /// do arrive in a later slice.
    /// </summary>
    private async Task<(Guid ApplicationId, Guid InterviewId)> FullyArmedAsync()
    {
        var applicationId = Guid.CreateVersion7();
        var interviewId = Guid.CreateVersion7();

        await SeedAsync(applicationId, [ReminderKind.FollowUp], interviewId: null);
        await SeedAsync(applicationId, ReminderInstants.InterviewKinds, interviewId);
        await SeedAsync(applicationId, DeadlineKinds, interviewId: null);

        return (applicationId, interviewId);
    }

    private async Task SeedAsync(
        Guid applicationId,
        IReadOnlyList<ReminderKind> kinds,
        Guid? interviewId,
        ReminderState state = ReminderState.Pending)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        foreach (var kind in kinds)
        {
            dbContext.Reminders.Add(new Reminder
            {
                OwnerId = UserId.New(),
                Kind = kind,
                State = state,
                DueAt = DueAt,
                ApplicationId = applicationId,
                InterviewId = interviewId,
                SourceRecordedAt = Armed,
            });
        }

        await dbContext.SaveChangesAsync(Ct);
    }

    // ------------------------------------------------------------------------ reading

    private async Task<IReadOnlyList<Reminder>> RemindersAsync(Guid applicationId, ReminderState state)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.ApplicationId == applicationId && reminder.State == state)
            .ToListAsync(Ct);
    }

    private async Task<IReadOnlyList<ReminderKind>> KindsAsync(Guid applicationId, ReminderState state) =>
        [.. (await RemindersAsync(applicationId, state)).Select(reminder => reminder.Kind).Order()];

    /// <summary>Every kind this application holds and where it ended up, in a stable order.</summary>
    private async Task<IReadOnlyList<string>> StatesByKindAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var rows = await dbContext.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.ApplicationId == applicationId)
            .Select(reminder => new { reminder.Kind, reminder.State })
            .ToListAsync(Ct);

        return [.. rows.Select(row => $"{row.Kind}:{row.State}").Order()];
    }
}
