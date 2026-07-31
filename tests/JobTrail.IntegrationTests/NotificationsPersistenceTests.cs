using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Notifications.Domain;
using JobTrail.Modules.Notifications.Persistence;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The reminder store against the real database. A reminder has no record anywhere
/// but this table - there is no scheduler entry beside it - so the shape of the
/// table is the shape of the feature, and the constraints below are the ones that
/// decide whether the same reminder can be armed twice or reach somebody twice.
/// Exercised through the context directly: the handlers that arm reminders, the
/// sweep that delivers them and the feed that lists them arrive in later slices.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsPersistenceTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// PostgreSQL keeps timestamps to the microsecond, so a value that made the
    /// round trip is equal to the one sent only once both are cut to the same
    /// precision.
    /// </summary>
    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);

    [Fact]
    public async Task Every_fact_an_armed_reminder_carries_round_trips()
    {
        var ownerId = UserId.New();
        var applicationId = Guid.CreateVersion7();
        var interviewId = Guid.CreateVersion7();
        var interviewAt = Truncate(DateTimeOffset.UtcNow.AddDays(3));
        var dueAt = Truncate(interviewAt.AddHours(-1));
        var recordedAt = Truncate(DateTimeOffset.UtcNow);

        Guid id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var reminder = new Reminder
            {
                OwnerId = ownerId,
                Kind = ReminderKind.InterviewHourBefore,
                DueAt = dueAt,
                ApplicationId = applicationId,
                InterviewId = interviewId,
                SubjectAt = interviewAt,
                SourceRecordedAt = recordedAt,
            };

            db.Reminders.Add(reminder);
            await db.SaveChangesAsync(Ct);

            id = reminder.Id;
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var reminder = await db.Reminders.SingleAsync(r => r.Id == id, Ct);

            reminder.OwnerId.ShouldBe(ownerId);
            reminder.Kind.ShouldBe(ReminderKind.InterviewHourBefore);
            reminder.DueAt.ShouldBe(dueAt);
            reminder.ApplicationId.ShouldBe(applicationId);
            reminder.InterviewId.ShouldBe(interviewId);
            reminder.SubjectAt.ShouldBe(interviewAt);
            reminder.SourceRecordedAt.ShouldBe(recordedAt);

            // Armed and waiting, without the caller having said so: the state is
            // defaulted at the database, so no insert can leave a reminder in a
            // state the sweep does not recognise.
            reminder.State.ShouldBe(ReminderState.Pending);

            reminder.Id.ShouldNotBe(Guid.Empty);    // uuidv7() assigned by the DB
            reminder.CreatedAt.ShouldNotBe(default); // now() assigned by the DB

            // Nothing raised it and it is not about a date.
            reminder.RuleId.ShouldBeNull();
            reminder.SubjectDate.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_deadline_reminder_keeps_its_subject_as_a_date()
    {
        // A deadline is a day, not a moment. Storing it as an instant would mean
        // inventing a time to write and a zone to read it back in - so the two
        // subjects are separate columns, and each kind fills the one that fits.
        var deadline = new DateOnly(2026, 9, 30);
        var applicationId = Guid.CreateVersion7();

        Guid id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var reminder = new Reminder
            {
                OwnerId = UserId.New(),
                Kind = ReminderKind.ApplicationDeadlineThreeDaysBefore,
                DueAt = Truncate(DateTimeOffset.UtcNow.AddDays(1)),
                ApplicationId = applicationId,
                SubjectDate = deadline,
                SourceRecordedAt = Truncate(DateTimeOffset.UtcNow),
            };

            db.Reminders.Add(reminder);
            await db.SaveChangesAsync(Ct);

            id = reminder.Id;
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var reminder = await db.Reminders.SingleAsync(r => r.Id == id, Ct);

            reminder.SubjectDate.ShouldBe(deadline);
            reminder.SubjectAt.ShouldBeNull();
            reminder.InterviewId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_round_holds_one_armed_reminder_of_each_kind()
    {
        var applicationId = Guid.CreateVersion7();
        var interviewId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            // The two alerts one interview gets. Same round, different moments, so
            // the slot has to tell them apart - a shape that could not would let
            // the hour-before overwrite the morning-before.
            db.Reminders.AddRange(
                Armed(applicationId, ReminderKind.InterviewMorningBefore, interviewId),
                Armed(applicationId, ReminderKind.InterviewHourBefore, interviewId));

            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Reminders.Add(Armed(applicationId, ReminderKind.InterviewHourBefore, interviewId));

            // Rescheduling replaces rather than appends, and this index is what
            // makes that the only option: a second armed reminder for the same
            // round and moment is refused by the database.
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task A_slot_with_no_interview_is_still_one_slot()
    {
        // The case the default would miss. Every kind except the two interview ones
        // leaves interview_id null, and Postgres treats nulls as distinct unless
        // told otherwise - so without NULLS NOT DISTINCT this index would accept
        // every duplicate it exists to refuse, silently.
        var applicationId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Reminders.Add(Armed(applicationId, ReminderKind.OfferDecisionMorningOf));
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.Reminders.Add(Armed(applicationId, ReminderKind.OfferDecisionMorningOf));

            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task A_retired_reminder_frees_its_slot()
    {
        // The reschedule, in miniature. The reminder that already fired stays
        // exactly as it was - it is a true record of what the owner was told - and
        // the new instant is a new row rather than an edit of that one. Only armed
        // reminders occupy a slot, which is what makes both possible.
        var applicationId = Guid.CreateVersion7();
        var interviewId = Guid.CreateVersion7();

        Guid firstId;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var first = Armed(applicationId, ReminderKind.InterviewMorningBefore, interviewId);
            db.Reminders.Add(first);
            await db.SaveChangesAsync(Ct);

            firstId = first.Id;

            first.State = ReminderState.Sent;
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            var second = Armed(applicationId, ReminderKind.InterviewMorningBefore, interviewId);
            second.DueAt = Truncate(DateTimeOffset.UtcNow.AddDays(7));
            db.Reminders.Add(second);

            await Should.NotThrowAsync(async () => await db.SaveChangesAsync(Ct));
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            (await db.Reminders.CountAsync(r => r.InterviewId == interviewId, Ct)).ShouldBe(2);
            (await db.Reminders.SingleAsync(r => r.Id == firstId, Ct)).State.ShouldBe(ReminderState.Sent);
        }
    }

    [Fact]
    public async Task A_reminder_is_delivered_once_per_channel()
    {
        var reminderId = await ArmAsync(Guid.CreateVersion7(), ReminderKind.FollowUp);
        var deliveredAt = Truncate(DateTimeOffset.UtcNow);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.ReminderDeliveries.Add(new ReminderDelivery
            {
                ReminderId = reminderId,
                Channel = DeliveryChannel.InApp,
                DeliveredAt = deliveredAt,
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.ReminderDeliveries.Add(new ReminderDelivery
            {
                ReminderId = reminderId,
                Channel = DeliveryChannel.InApp,
                DeliveredAt = Truncate(DateTimeOffset.UtcNow),
            });

            // At-least-once means a second sweep can claim the same reminder. The
            // key is what makes that a refused insert instead of a second nudge.
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(Ct));
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var delivery = await db.ReminderDeliveries.SingleAsync(d => d.ReminderId == reminderId, Ct);

            // When the owner was actually told, which is not when it was due - the
            // one fact the reminder row cannot hold.
            delivery.DeliveredAt.ShouldBe(deliveredAt);
        }
    }

    [Fact]
    public async Task Deleting_a_reminder_takes_its_deliveries_with_it()
    {
        var reminderId = await ArmAsync(Guid.CreateVersion7(), ReminderKind.ApplicationDeadlineMorningOf);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.ReminderDeliveries.Add(new ReminderDelivery
            {
                ReminderId = reminderId,
                Channel = DeliveryChannel.InApp,
                DeliveredAt = Truncate(DateTimeOffset.UtcNow),
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.Reminders.Where(r => r.Id == reminderId).ExecuteDeleteAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            // A delivery says nothing without the reminder it delivered, so erasure
            // reaches it through the parent rather than having to name it.
            (await db.ReminderDeliveries.AnyAsync(d => d.ReminderId == reminderId, Ct)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task An_account_automates_follow_ups_once()
    {
        var ownerId = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.ReminderRules.Add(NewRule(ownerId));
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.ReminderRules.Add(NewRule(ownerId));

            // One rule per account is the database's rule, not a handler's.
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(Ct));
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var rule = await db.ReminderRules.SingleAsync(r => r.OwnerId == ownerId, Ct);

            rule.DaysAfterApplied.ShouldBe(7);
            rule.Id.ShouldNotBe(Guid.Empty);        // uuidv7() assigned by the DB
            rule.CreatedAt.ShouldNotBe(default);     // now() assigned by the DB
        }
    }

    [Fact]
    public async Task A_follow_up_outlives_the_rule_that_raised_it()
    {
        var ownerId = UserId.New();
        var applicationId = Guid.CreateVersion7();

        Guid ruleId;
        Guid reminderId;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var rule = NewRule(ownerId);
            db.ReminderRules.Add(rule);
            await db.SaveChangesAsync(Ct);

            ruleId = rule.Id;

            var reminder = Armed(applicationId, ReminderKind.FollowUp);
            reminder.OwnerId = ownerId;
            reminder.RuleId = ruleId;
            db.Reminders.Add(reminder);
            await db.SaveChangesAsync(Ct);

            reminderId = reminder.Id;
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await db.ReminderRules.Where(r => r.Id == ruleId).ExecuteDeleteAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var reminder = await db.Reminders.SingleAsync(r => r.Id == reminderId, Ct);

            // Turning the automation off stops it raising more follow-ups. It does
            // not rewrite what the owner has already been told, which is why the
            // link nulls out instead of cascading.
            reminder.RuleId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Reminders_are_read_back_by_owner()
    {
        // The access path the feed and the erasure both take, and the reason the
        // feed's index leads on the owner.
        var mine = UserId.New();
        var theirs = UserId.New();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            var first = Armed(Guid.CreateVersion7(), ReminderKind.InterviewMorningBefore, Guid.CreateVersion7());
            first.OwnerId = mine;
            var second = Armed(Guid.CreateVersion7(), ReminderKind.OfferDecisionDayBefore);
            second.OwnerId = mine;
            var third = Armed(Guid.CreateVersion7(), ReminderKind.FollowUp);
            third.OwnerId = theirs;

            db.Reminders.AddRange(first, second, third);
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

            (await db.Reminders.CountAsync(r => r.OwnerId == mine, Ct)).ShouldBe(2);
            (await db.Reminders.CountAsync(r => r.OwnerId == theirs, Ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Every_fact_a_tracked_application_carries_round_trips()
    {
        var applicationId = Guid.CreateVersion7();
        var ownerId = UserId.New();
        var appliedDate = new DateOnly(2026, 5, 4);
        var recordedAt = Truncate(DateTimeOffset.UtcNow);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.TrackedApplications.Add(new TrackedApplication
            {
                ApplicationId = applicationId,
                OwnerId = ownerId,
                AppliedDate = appliedDate,
                Stage = "Screening",
                StageRecordedAt = recordedAt,
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var tracked = await db.TrackedApplications.SingleAsync(t => t.ApplicationId == applicationId, Ct);

            tracked.OwnerId.ShouldBe(ownerId);
            tracked.AppliedDate.ShouldBe(appliedDate);
            tracked.Stage.ShouldBe("Screening");
            tracked.StageRecordedAt.ShouldBe(recordedAt);
            tracked.CreatedAt.ShouldNotBe(default); // now() assigned by the DB
        }
    }

    [Fact]
    public async Task A_tracked_application_exists_before_its_applied_date_is_known()
    {
        // The case that forces applied_date to be nullable, against the instinct
        // that every application obviously has one. Only ApplicationSubmitted
        // carries it and delivery is unordered, so an application whose stage change
        // arrives first has to be recordable without it. Refusing the row would drop
        // the stage change, and this table would then go on believing the
        // application was still waiting for an answer it had already had.
        var applicationId = Guid.CreateVersion7();
        var recordedAt = Truncate(DateTimeOffset.UtcNow);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.TrackedApplications.Add(new TrackedApplication
            {
                ApplicationId = applicationId,
                OwnerId = UserId.New(),
                Stage = "Interview",
                StageRecordedAt = recordedAt,
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var tracked = await db.TrackedApplications.SingleAsync(t => t.ApplicationId == applicationId, Ct);

            tracked.AppliedDate.ShouldBeNull();
            tracked.Stage.ShouldBe("Interview");
        }
    }

    [Fact]
    public async Task A_tracked_application_keeps_the_key_the_event_carried()
    {
        // Unlike the reminders this module raises itself, this key arrives on the
        // event and must survive untouched: a key the database rewrote would insert
        // a second row for the same application on every redelivery, and the scan
        // would nudge about it twice.
        var applicationId = Guid.CreateVersion7();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            db.TrackedApplications.Add(new TrackedApplication
            {
                ApplicationId = applicationId,
                OwnerId = UserId.New(),
            });
            await db.SaveChangesAsync(Ct);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            (await db.TrackedApplications.AnyAsync(t => t.ApplicationId == applicationId, Ct)).ShouldBeTrue();
        }
    }

    private static Reminder Armed(Guid applicationId, ReminderKind kind, Guid? interviewId = null) => new()
    {
        OwnerId = UserId.New(),
        Kind = kind,
        DueAt = Truncate(DateTimeOffset.UtcNow.AddDays(1)),
        ApplicationId = applicationId,
        InterviewId = interviewId,
        SourceRecordedAt = Truncate(DateTimeOffset.UtcNow),
    };

    private static ReminderRule NewRule(UserId ownerId) => new()
    {
        OwnerId = ownerId,
        DaysAfterApplied = 7,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<Guid> ArmAsync(Guid applicationId, ReminderKind kind)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var reminder = Armed(applicationId, kind);
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(Ct);

        return reminder.Id;
    }
}
