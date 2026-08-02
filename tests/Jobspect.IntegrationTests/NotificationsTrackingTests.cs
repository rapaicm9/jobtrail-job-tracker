using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.Modules.Notifications.Domain;
using Jobspect.Modules.Notifications.Features.TrackApplications;
using Jobspect.Modules.Notifications.Persistence;
using Jobspect.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The small record this module keeps of the applications it watches for silence -
/// the input the follow-up scan will read, and the reason it can run in a process
/// that does not host the module those applications belong to.
/// <para>
/// Nothing here arms a reminder. A follow-up is raised by the scan from a rule the
/// account may not have created yet; arming one on submission would leave a rule
/// set up afterwards with nothing to act on.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsTrackingTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset T1 = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = fixture.CreateClient();

    // ---------------------------------------------------------------- wiring

    [Fact]
    public async Task A_recorded_application_becomes_something_to_watch()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var created = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Platform Engineer",
            appliedDate = "2026-03-01",
        })).ReadApplicationAsync();

        var tracked = await PollForTrackedAsync(created.Id, row => row.AppliedDate is not null);

        tracked.OwnerId.ShouldBe(UserId.From(tokens.UserId));
        tracked.AppliedDate.ShouldBe(new DateOnly(2026, 3, 1));

        // Nothing has said it moved, which is the ordinary case. The module does
        // not assert where an application starts - that is the pipeline's knowledge.
        tracked.Stage.ShouldBeNull();
    }

    [Fact]
    public async Task A_transition_records_where_the_application_went()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);

        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        (await _client.TransitionApplicationAsync(tokens.AccessToken, created.Id, "Screening"))
            .IsSuccessStatusCode.ShouldBeTrue();

        var tracked = await PollForTrackedAsync(created.Id, row => row.Stage is not null);

        tracked.Stage.ShouldBe("Screening");
        tracked.StageRecordedAt.ShouldNotBeNull();
    }

    // ------------------------------------------------------------ the upserts

    [Fact]
    public async Task An_out_of_order_stage_change_does_not_put_it_back()
    {
        var applicationId = Guid.CreateVersion7();
        var ownerId = UserId.New();

        await StageChangedAsync(applicationId, ownerId, "Interview", T2);
        await StageChangedAsync(applicationId, ownerId, "Screening", T1);

        // Without the guard this row would say the application is still waiting for
        // an answer it has already had - and the scan would nudge about it.
        (await TrackedAsync(applicationId))!.Stage.ShouldBe("Interview");
    }

    /// <summary>
    /// Only the submission carries the applied date, so an application whose stage
    /// change is delivered first exists here before that date is known. Refusing the
    /// row would drop the stage change - which is why the column is nullable.
    /// </summary>
    [Fact]
    public async Task A_stage_change_arriving_first_creates_the_row_and_the_submission_fills_it_in()
    {
        var applicationId = Guid.CreateVersion7();
        var ownerId = UserId.New();

        await StageChangedAsync(applicationId, ownerId, "Screening", T2);

        var early = await TrackedAsync(applicationId);
        early.ShouldNotBeNull();
        early.AppliedDate.ShouldBeNull();
        early.Stage.ShouldBe("Screening");

        await SubmittedAsync(applicationId, ownerId, new DateOnly(2026, 3, 1), T1);

        var settled = await TrackedAsync(applicationId);
        settled!.AppliedDate.ShouldBe(new DateOnly(2026, 3, 1));

        // The submission carries no stage, so it must not clear the one recorded.
        settled.Stage.ShouldBe("Screening");
    }

    [Fact]
    public async Task Redelivery_leaves_the_row_as_it_was()
    {
        var applicationId = Guid.CreateVersion7();
        var ownerId = UserId.New();

        await SubmittedAsync(applicationId, ownerId, new DateOnly(2026, 3, 1), T1);
        await StageChangedAsync(applicationId, ownerId, "Screening", T2);

        await SubmittedAsync(applicationId, ownerId, new DateOnly(2026, 3, 1), T1);
        await StageChangedAsync(applicationId, ownerId, "Screening", T2);

        var tracked = await TrackedAsync(applicationId);

        tracked!.AppliedDate.ShouldBe(new DateOnly(2026, 3, 1));
        tracked.Stage.ShouldBe("Screening");
        (await CountAsync(applicationId)).ShouldBe(1);
    }

    // ----------------------------------------------------------------- driving

    private async Task SubmittedAsync(
        Guid applicationId, UserId ownerId, DateOnly appliedDate, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await new ApplicationSubmittedTracker(new TrackedApplicationWriter(dbContext)).HandleAsync(
            new ApplicationSubmitted(
                Guid.CreateVersion7(),
                applicationId,
                ownerId,
                CampaignId: Guid.CreateVersion7(),
                CompanyId: null,
                appliedDate,
                Source: null,
                WorkMode: null,
                occurredAt),
            Ct);
    }

    private async Task StageChangedAsync(
        Guid applicationId, UserId ownerId, string to, DateTimeOffset occurredAt)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await new ApplicationStageChangedTracker(new TrackedApplicationWriter(dbContext)).HandleAsync(
            new ApplicationStageChanged(
                Guid.CreateVersion7(), applicationId, ownerId, From: "Applied", To: to, occurredAt),
            Ct);
    }

    // ----------------------------------------------------------------- reading

    private async Task<TrackedApplication?> TrackedAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.TrackedApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(tracked => tracked.ApplicationId == applicationId, Ct);
    }

    private async Task<int> CountAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        return await dbContext.TrackedApplications
            .CountAsync(tracked => tracked.ApplicationId == applicationId, Ct);
    }

    private async Task<TrackedApplication> PollForTrackedAsync(
        Guid applicationId, Func<TrackedApplication, bool> until)
    {
        await Poll.UntilAsync(
            async () => await TrackedAsync(applicationId) is { } tracked && until(tracked),
            "the outbox should deliver the event and fill the tracked row",
            Ct);

        return (await TrackedAsync(applicationId))!;
    }
}
