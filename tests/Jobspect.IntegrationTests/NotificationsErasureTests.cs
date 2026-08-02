using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications;
using Jobspect.Modules.Identity.Contracts;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.EraseData;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// This module's share of the erasure fan-out. It ships with the consumers rather
/// than after them, for the reason Analytics' did: from the moment these tables are
/// filled, an account deletion that leaves them behind reports success while the
/// data remains - and here the data goes on to notify somebody.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsErasureTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task An_account_deletion_takes_its_reminders_with_it()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(
            _client, Ct, timeZoneId: "Europe/Belgrade");
        var ownerId = UserId.From(tokens.UserId);

        var created = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Engineer",
            applicationDeadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        })).ReadApplicationAsync();

        // Wait for the rows, or the erasure below would prove nothing.
        await Poll.UntilAsync(
            async () => await ReminderCountAsync(ownerId) > 0 && await TrackedCountAsync(ownerId) > 0,
            "the account should hold reminders and a tracked application before it is erased",
            Ct);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await Poll.UntilAsync(
            async () => await ReminderCountAsync(ownerId) == 0 && await TrackedCountAsync(ownerId) == 0,
            "deleting the account should erase the reminders and the tracked applications behind it",
            Ct);

        (await RemindersForApplicationAsync(created.Id)).ShouldBe(0);
    }

    /// <summary>
    /// A delivery says nothing without its reminder, so the foreign key cascades and
    /// the database takes it. Asserted rather than assumed: the handler deliberately
    /// does not delete deliveries, and if the cascade were ever dropped from the
    /// migration this is the only test that would notice.
    /// </summary>
    [Fact]
    public async Task A_delivery_goes_with_the_reminder_it_records()
    {
        var ownerId = UserId.New();
        var reminderId = await SeedReminderAsync(ownerId);

        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            dbContext.ReminderDeliveries.Add(new ReminderDelivery
            {
                ReminderId = reminderId,
                Channel = DeliveryChannel.InApp,
                DeliveredAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync(Ct);
        }

        (await DeliveryCountAsync(reminderId)).ShouldBe(1);

        await EraseAsync(ownerId);

        (await DeliveryCountAsync(reminderId)).ShouldBe(0);
    }

    [Fact]
    public async Task It_leaves_another_accounts_rows_alone()
    {
        var mine = UserId.New();
        var theirs = UserId.New();

        await SeedReminderAsync(mine);
        await SeedTrackedAsync(mine);
        await SeedReminderAsync(theirs);
        await SeedTrackedAsync(theirs);

        await EraseAsync(mine);

        (await ReminderCountAsync(mine)).ShouldBe(0);
        (await TrackedCountAsync(mine)).ShouldBe(0);

        (await ReminderCountAsync(theirs)).ShouldBe(1);
        (await TrackedCountAsync(theirs)).ShouldBe(1);
    }

    [Fact]
    public async Task Erasing_twice_is_not_an_error()
    {
        // At-least-once delivery means the handler can be asked to do this again.
        var ownerId = UserId.New();
        await SeedReminderAsync(ownerId);

        await EraseAsync(ownerId);
        await Should.NotThrowAsync(EraseAsync(ownerId));

        (await ReminderCountAsync(ownerId)).ShouldBe(0);
    }

    [Fact]
    public async Task Erasing_an_account_that_has_nothing_here_is_not_an_error() =>
        await Should.NotThrowAsync(EraseAsync(UserId.New()));

    /// <summary>
    /// The ordering the fan-out depends on: this module's handler has to run after
    /// the Applications module's, which deletes the events still owed on the
    /// account's behalf. Reversed, an owed <c>InterviewScheduled</c> delivered in
    /// the gap would arm a reminder for an account that had just been erased - and
    /// unlike a stale read-model row, that one goes on to notify somebody.
    /// <para>
    /// Handlers run in registration order, so the guarantee is really about where
    /// the host calls <c>AddNotificationsConsumers</c> - invisible at the call site,
    /// and exactly the sort of thing a tidy-up reorders.
    /// </para>
    /// </summary>
    [Fact]
    public void Its_erasure_runs_after_the_module_whose_events_it_consumes()
    {
        using var scope = fixture.CreateScope();

        var handlers = scope.ServiceProvider
            .GetServices<Jobspect.SharedKernel.Events.IEventHandler<UserDataDeletionRequested>>()
            .Select(handler => handler.GetType())
            .ToList();

        var applications = handlers.FindIndex(type => type.Assembly == typeof(ApplicationsModule).Assembly);
        var notifications = handlers.FindIndex(type => type == typeof(NotificationsDataErasureHandler));

        applications.ShouldBeGreaterThanOrEqualTo(0, "the Applications module should erase its own data");
        notifications.ShouldBeGreaterThan(
            applications,
            "Notifications must erase after Applications has removed the events still owed for the account");
    }

    // ----------------------------------------------------------------- driving

    private async Task EraseAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await new NotificationsDataErasureHandler(dbContext)
            .HandleAsync(new UserDataDeletionRequested(Guid.CreateVersion7(), ownerId), Ct);
    }

    private async Task<Guid> SeedReminderAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var reminder = new Reminder
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Kind = ReminderKind.ApplicationDeadlineMorningOf,
            State = ReminderState.Pending,
            DueAt = DateTimeOffset.UtcNow.AddDays(10),
            ApplicationId = Guid.CreateVersion7(),
            SubjectDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            SourceRecordedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync(Ct);

        return reminder.Id;
    }

    private async Task SeedTrackedAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        dbContext.TrackedApplications.Add(new TrackedApplication
        {
            ApplicationId = Guid.CreateVersion7(),
            OwnerId = ownerId,
            AppliedDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        await dbContext.SaveChangesAsync(Ct);
    }

    // ----------------------------------------------------------------- reading

    private async Task<int> ReminderCountAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders.CountAsync(reminder => reminder.OwnerId == ownerId, Ct);
    }

    private async Task<int> TrackedCountAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.TrackedApplications.CountAsync(tracked => tracked.OwnerId == ownerId, Ct);
    }

    private async Task<int> RemindersForApplicationAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.Reminders.CountAsync(reminder => reminder.ApplicationId == applicationId, Ct);
    }

    private async Task<int> DeliveryCountAsync(Guid reminderId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.ReminderDeliveries.CountAsync(delivery => delivery.ReminderId == reminderId, Ct);
    }
}
