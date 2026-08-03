using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.SweepReminders;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The sweep's rules, driven directly at a time of this suite's choosing.
/// <para>
/// The sweep runs on a Quartz trigger in the worker, which is precisely why nothing
/// here goes through one: every rule below is a statement about how late is too late,
/// and a test that could not decide what time it was would be asserting against a
/// stopwatch. So the work is a plain class taking its clock as an argument, exactly
/// as the arming handlers do, and the job that runs it in production is a three-line
/// adapter. That the adapter is wired up is proven once, on the real clock, in
/// <see cref="NotificationsSchedulerTests"/>.
/// </para>
/// <para>
/// Nothing sweeps behind these tests' backs: the API host composes the reminder store
/// and the consumers, and never the schedule.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsSweepTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The moment every test here sweeps at. Whole seconds, so nothing depends on how
    /// PostgreSQL rounds.
    /// <para>
    /// Deliberately years <em>behind</em> the real clock, and that is the isolation.
    /// A sweep is global by nature - it reads the whole table, since a reminder due
    /// now is due now whoever owns it - so a pass at a clock ahead of the suite would
    /// deliver and drop rows the rest of the suite is still asserting about. At this
    /// reading, everything armed anywhere else is comfortably in the future and
    /// invisible, and only the rows written below are due.
    /// </para>
    /// </summary>
    private static readonly DateTimeOffset Now = new(2020, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_due_reminder_is_delivered_and_recorded()
    {
        var applicationId = await ArmedAsync(dueAt: Now);

        await SweepAsync();

        var reminder = await ReminderAsync(applicationId);
        reminder.State.ShouldBe(ReminderState.Sent);

        var delivery = await DeliveryAsync(reminder.Id);
        delivery.ShouldNotBeNull();
        delivery.Channel.ShouldBe(DeliveryChannel.InApp);

        // When the owner was told, which is not when the reminder was due - and it is
        // the sweep's own reading of the clock, not the database's. The column carries
        // no default precisely so this is assertable.
        delivery.DeliveredAt.ShouldBe(Now);
    }

    /// <summary>
    /// The tolerance cannot be zero - the sweep discovers everything slightly after
    /// its instant - so the boundary is a rule, and a rule is worth pinning at its
    /// edge rather than somewhere comfortably inside it.
    /// </summary>
    [Fact]
    public async Task A_reminder_exactly_at_the_tolerance_is_still_delivered()
    {
        var applicationId = await ArmedAsync(dueAt: Now.AddMinutes(-10));

        await SweepAsync();

        (await ReminderAsync(applicationId)).State.ShouldBe(ReminderState.Sent);
    }

    [Fact]
    public async Task A_reminder_past_the_tolerance_is_dropped_and_never_delivered()
    {
        var applicationId = await ArmedAsync(dueAt: Now.AddMinutes(-11));

        await SweepAsync();

        var reminder = await ReminderAsync(applicationId);

        // Dropped rather than Cancelled: nobody retracted this one, it was owed and
        // missed, and the two answer different questions when the feed is quiet.
        reminder.State.ShouldBe(ReminderState.Dropped);

        // And no delivery at all. Being told about something that has already happened
        // is worse than silence.
        (await DeliveryAsync(reminder.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task A_reminder_not_yet_due_is_left_alone()
    {
        var applicationId = await ArmedAsync(dueAt: Now.AddMinutes(1));

        await SweepAsync();

        var reminder = await ReminderAsync(applicationId);
        reminder.State.ShouldBe(ReminderState.Pending);
        (await DeliveryAsync(reminder.Id)).ShouldBeNull();
    }

    /// <summary>
    /// A retracted reminder is long past its instant and must stay retracted. The
    /// sweep reads one state and one only; anything looser would have it announcing
    /// interviews that were cancelled weeks ago.
    /// </summary>
    [Fact]
    public async Task A_retracted_reminder_is_not_resurrected()
    {
        var applicationId = await ArmedAsync(dueAt: Now.AddDays(-30), state: ReminderState.Cancelled);

        await SweepAsync();

        var reminder = await ReminderAsync(applicationId);
        reminder.State.ShouldBe(ReminderState.Cancelled);
        (await DeliveryAsync(reminder.Id)).ShouldBeNull();
    }

    /// <summary>
    /// The guard the delivery key exists for. Sweeping twice proves nothing on its
    /// own - the first pass moves the row out of <c>Pending</c> and the second never
    /// looks at it - so the row is put back and swept again, which is the shape the
    /// key is there to refuse: a second attempt at a reminder already delivered.
    /// </summary>
    [Fact]
    public async Task A_second_delivery_of_the_same_reminder_is_refused_rather_than_repeated()
    {
        var applicationId = await ArmedAsync(dueAt: Now);

        await SweepAsync();
        var reminderId = (await ReminderAsync(applicationId)).Id;

        await ReArmAsync(reminderId);
        await SweepAsync();

        var deliveries = await DeliveriesAsync(reminderId);

        // One delivery, and the original one: ON CONFLICT DO NOTHING means the second
        // attempt neither inserts a row nor overwrites when the owner was told.
        deliveries.Count.ShouldBe(1);
        deliveries[0].DeliveredAt.ShouldBe(Now);

        (await ReminderAsync(applicationId)).State.ShouldBe(ReminderState.Sent);
    }

    /// <summary>
    /// One claim takes a bounded batch, so a backlog is only cleared in a single pass
    /// because the sweep keeps claiming until a batch comes back short. Without that
    /// loop this leaves a hundred delivered and the rest waiting a minute each.
    /// </summary>
    [Fact]
    public async Task A_backlog_larger_than_one_batch_drains_in_one_pass()
    {
        // Comfortably more than one claim takes, so at least two are needed.
        const int backlog = 150;

        var ownerId = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            for (var index = 0; index < backlog; index++)
            {
                // An application of its own per row: these all share a kind, and the
                // slot index would refuse a second armed row for the same application.
                dbContext.Reminders.Add(NewReminder(Guid.CreateVersion7(), ownerId, Now, ReminderState.Pending));
            }

            await dbContext.SaveChangesAsync(Ct);
        }

        await SweepAsync();

        using var reading = fixture.CreateScope();
        var read = reading.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var states = await read.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.OwnerId == ownerId)
            .Select(reminder => reminder.State)
            .ToListAsync(Ct);

        states.Count.ShouldBe(backlog);
        states.ShouldAllBe(state => state == ReminderState.Sent);
    }

    // ----------------------------------------------------------------- driving

    private async Task SweepAsync()
    {
        using var scope = fixture.CreateScope();
        var provider = scope.ServiceProvider;

        var sweep = new ReminderSweep(
            provider.GetRequiredService<NotificationsDbContext>(),
            new FixedClock(Now),
            provider.GetRequiredService<ILogger<ReminderSweep>>());

        await sweep.SweepAsync(Ct);
    }

    /// <summary>Puts a delivered reminder back where the sweep will find it again.</summary>
    private async Task ReArmAsync(Guid reminderId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await dbContext.Reminders
            .Where(reminder => reminder.Id == reminderId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(reminder => reminder.State, ReminderState.Pending), Ct);
    }

    private async Task<Guid> ArmedAsync(DateTimeOffset dueAt, ReminderState state = ReminderState.Pending)
    {
        var applicationId = Guid.CreateVersion7();

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        dbContext.Reminders.Add(NewReminder(applicationId, UserId.New(), dueAt, state));
        await dbContext.SaveChangesAsync(Ct);

        return applicationId;
    }

    /// <summary>
    /// A reminder as the arming handlers would have left one, minus the event that
    /// decided it. Written directly rather than armed through a handler because these
    /// tests are about instants that have already passed, which arming refuses to
    /// create.
    /// </summary>
    private static Reminder NewReminder(
        Guid applicationId, UserId ownerId, DateTimeOffset dueAt, ReminderState state) =>
        new()
        {
            OwnerId = ownerId,
            Kind = ReminderKind.ApplicationDeadlineMorningOf,
            State = state,
            DueAt = dueAt,
            ApplicationId = applicationId,
            SubjectDate = DateOnly.FromDateTime(dueAt.UtcDateTime),
            SourceRecordedAt = dueAt.AddDays(-1),
        };

    // ----------------------------------------------------------------- reading

    private async Task<Reminder> ReminderAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders
            .AsNoTracking()
            .SingleAsync(reminder => reminder.ApplicationId == applicationId, Ct);
    }

    private async Task<ReminderDelivery?> DeliveryAsync(Guid reminderId) =>
        (await DeliveriesAsync(reminderId)).SingleOrDefault();

    private async Task<IReadOnlyList<ReminderDelivery>> DeliveriesAsync(Guid reminderId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.ReminderDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.ReminderId == reminderId)
            .ToListAsync(Ct);
    }
}
