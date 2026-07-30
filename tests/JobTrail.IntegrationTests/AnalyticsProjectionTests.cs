using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Analytics.Domain;
using JobTrail.Modules.Analytics.Features.ProjectApplicationFacts;
using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel;
using JobTrail.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The read model being filled from the events, in two layers.
/// <para>
/// The first is the wiring: a real request over HTTP, and the base row appearing
/// behind it through the real outbox. The second is the arithmetic, driven by
/// handing the projections events directly - because the cases worth proving are
/// redelivery and out-of-order arrival, and a live dispatcher cannot be asked to
/// produce either on demand. Both run against the real database; the upserts are
/// SQL, so there is nothing here a fake could answer for.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsProjectionTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Whole seconds, so nothing here depends on how PostgreSQL rounds.
    private static readonly DateTimeOffset T1 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 3, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = fixture.CreateClient();

    // ---------------------------------------------------------------- wiring

    [Fact]
    public async Task A_recorded_application_reaches_the_read_model()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var campaignId = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);

        var created = await (await _client.CreateApplicationAsync(tokens.AccessToken, new
        {
            role = "Platform Engineer",
            source = "LinkedIn",
            workMode = "hybrid",
            appliedDate = "2026-03-01",
        })).ReadApplicationAsync();

        var facts = await WaitForFactsAsync(created.Id, f => f.AppliedDate is not null);

        facts.OwnerId.ShouldBe(UserId.From(tokens.UserId));
        facts.CampaignId.ShouldBe(campaignId);
        facts.AppliedDate.ShouldBe(new DateOnly(2026, 3, 1));
        facts.Source.ShouldBe("LinkedIn");
        facts.WorkMode.ShouldBe("Hybrid");

        // A brand-new application has no transition to announce, so the row's
        // stage comes from the submission - without it the pipeline snapshot
        // would not count it at all.
        facts.Stage.ShouldBe("Applied");
        facts.StageEnteredAt.ShouldNotBeNull();

        // Nothing has happened to it yet.
        facts.Outcome.ShouldBeNull();
        facts.FirstResponseAt.ShouldBeNull();
        facts.ReachedScreeningAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_transition_moves_the_row_and_fills_the_funnel()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await ShouldSucceedAsync(
            _client.TransitionApplicationAsync(tokens.AccessToken, created.Id, "Screening"));

        var facts = await WaitForFactsAsync(created.Id, f => f.Stage == "Screening");

        facts.ReachedScreeningAt.ShouldNotBeNull();
        facts.FirstResponseAt.ShouldBe(facts.ReachedScreeningAt);
        facts.ClosedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Closing_an_application_records_the_outcome()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await ShouldSucceedAsync(
            _client.TransitionApplicationAsync(tokens.AccessToken, created.Id, "Rejected"));

        // The stage change and the closure are announced as two events in one
        // transaction, and both have to land - they carry the same instant, so a
        // strict "newer wins" comparison would drop whichever arrived second.
        var facts = await WaitForFactsAsync(created.Id, f => f.Outcome is not null);

        facts.Stage.ShouldBe("Rejected");
        facts.Outcome.ShouldBe("Rejected");
        facts.ClosedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Moving_campaign_follows_the_application()
    {
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var destination = await (await _client.CreateCampaignAsync(
            tokens.AccessToken, new { name = "Second search" })).ReadCampaignAsync();

        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await ShouldSucceedAsync(_client.UpdateApplicationAsync(tokens.AccessToken, created.Id, new
        {
            role = created.Role,
            campaignId = destination.Id,
            appliedDate = created.AppliedDate.ToString("O"),
        }));

        var facts = await WaitForFactsAsync(created.Id, f => f.CampaignId == destination.Id);
        facts.CampaignId.ShouldBe(destination.Id);
    }

    // ------------------------------------------------------------- arithmetic

    [Fact]
    public async Task Redelivering_an_event_changes_nothing()
    {
        var (id, owner) = NewApplication();
        var moved = new ApplicationStageChanged(Guid.CreateVersion7(), id, owner, "Applied", "Screening", T1);

        await ApplyAsync(moved);
        var once = await FactsAsync(id);

        await ApplyAsync(moved);
        var twice = await FactsAsync(id);

        twice.Stage.ShouldBe(once.Stage);
        twice.StageEnteredAt.ShouldBe(once.StageEnteredAt);
        twice.StageRecordedAt.ShouldBe(once.StageRecordedAt);
        twice.ReachedScreeningAt.ShouldBe(once.ReachedScreeningAt);
        twice.FirstResponseAt.ShouldBe(once.FirstResponseAt);
    }

    [Fact]
    public async Task An_older_move_arriving_late_does_not_win()
    {
        var (id, owner) = NewApplication();

        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), id, owner, "Screening", "Interview", T2));
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), id, owner, "Applied", "Screening", T1));

        // Back to front, and the row still says where the application actually is.
        var facts = await FactsAsync(id);
        facts.Stage.ShouldBe("Interview");
        facts.StageEnteredAt.ShouldBe(T2);
    }

    [Fact]
    public async Task An_older_move_arriving_late_still_fills_its_own_funnel_step()
    {
        // The regression test for the shape this was nearly built with. Guarding
        // the whole update with a WHERE would make a stale event skip everything,
        // including the funnel timestamps - which are supposed to be immune to
        // ordering. The stage must not move backwards, and the step must still be
        // recorded.
        var (id, owner) = NewApplication();

        await ApplyAsync(new ApplicationStageChanged(Guid.CreateVersion7(), id, owner, "Applied", "Offer", T2));
        await ApplyAsync(new ApplicationStageChanged(Guid.CreateVersion7(), id, owner, "Applied", "Screening", T1));

        var facts = await FactsAsync(id);

        facts.Stage.ShouldBe("Offer");
        facts.ReachedOfferAt.ShouldBe(T2);
        facts.ReachedScreeningAt.ShouldBe(T1);

        // And the earliest response wins, whichever order the moves arrived in.
        facts.FirstResponseAt.ShouldBe(T1);
    }

    [Fact]
    public async Task The_funnel_reads_the_same_whichever_order_the_moves_arrive_in()
    {
        var (forwardId, forwardOwner) = NewApplication();
        var (reverseId, reverseOwner) = NewApplication();

        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), forwardId, forwardOwner, "Applied", "Screening", T1));
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), forwardId, forwardOwner, "Screening", "Interview", T2));

        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), reverseId, reverseOwner, "Screening", "Interview", T2));
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), reverseId, reverseOwner, "Applied", "Screening", T1));

        var forward = await FactsAsync(forwardId);
        var reverse = await FactsAsync(reverseId);

        reverse.ReachedScreeningAt.ShouldBe(forward.ReachedScreeningAt);
        reverse.ReachedInterviewAt.ShouldBe(forward.ReachedInterviewAt);
        reverse.FirstResponseAt.ShouldBe(forward.FirstResponseAt);
    }

    [Fact]
    public async Task A_reopening_clears_the_outcome_whichever_order_it_arrives_in()
    {
        var (inOrderId, inOrderOwner) = NewApplication();
        await ApplyAsync(new ApplicationReachedTerminal(
            Guid.CreateVersion7(), inOrderId, inOrderOwner, "Screening", "Rejected", T1));
        await ApplyAsync(new ApplicationReopened(
            Guid.CreateVersion7(), inOrderId, inOrderOwner, "Rejected", "Applied", T2));

        var reopened = await FactsAsync(inOrderId);
        reopened.Outcome.ShouldBeNull();
        reopened.ClosedAt.ShouldBeNull();

        // The same two the other way round: a closure redelivered after the
        // reopening must not re-close it.
        var (reversedId, reversedOwner) = NewApplication();
        await ApplyAsync(new ApplicationReopened(
            Guid.CreateVersion7(), reversedId, reversedOwner, "Rejected", "Applied", T2));
        await ApplyAsync(new ApplicationReachedTerminal(
            Guid.CreateVersion7(), reversedId, reversedOwner, "Screening", "Rejected", T1));

        var stillOpen = await FactsAsync(reversedId);
        stillOpen.Outcome.ShouldBeNull();
        stillOpen.ClosedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_move_and_the_closure_it_amounts_to_share_an_instant_and_both_land()
    {
        // The pair a terminal transition writes in one transaction. They carry the
        // same OccurredAt, so a strictly-newer comparison would let whichever
        // arrived first discard the other; they write disjoint columns, so a tie
        // has to apply.
        var (id, owner) = NewApplication();

        await ApplyAsync(new ApplicationReachedTerminal(
            Guid.CreateVersion7(), id, owner, "Applied", "Ghosted", T1));
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), id, owner, "Applied", "Ghosted", T1));

        var facts = await FactsAsync(id);
        facts.Stage.ShouldBe("Ghosted");
        facts.Outcome.ShouldBe("Ghosted");
        facts.ClosedAt.ShouldBe(T1);
    }

    [Fact]
    public async Task Silence_and_the_users_own_withdrawal_are_not_responses()
    {
        var (ghosted, ghostedOwner) = NewApplication();
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), ghosted, ghostedOwner, "Applied", "Ghosted", T1));
        (await FactsAsync(ghosted)).FirstResponseAt.ShouldBeNull();

        var (withdrawn, withdrawnOwner) = NewApplication();
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), withdrawn, withdrawnOwner, "Applied", "Withdrawn", T1));
        (await FactsAsync(withdrawn)).FirstResponseAt.ShouldBeNull();

        // A rejection is an answer, unwelcome as it is.
        var (rejected, rejectedOwner) = NewApplication();
        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), rejected, rejectedOwner, "Applied", "Rejected", T1));
        (await FactsAsync(rejected)).FirstResponseAt.ShouldBe(T1);
    }

    [Fact]
    public async Task A_row_that_starts_from_a_transition_heals_when_its_submission_lands()
    {
        // The case the nullable columns exist for. The stage change creates the
        // row without a campaign or an applied date; the submission fills them in
        // without disturbing where the application has got to.
        var (id, owner) = NewApplication();
        var campaignId = Guid.CreateVersion7();

        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), id, owner, "Applied", "Interview", T2));

        var partial = await FactsAsync(id);
        partial.CampaignId.ShouldBeNull();
        partial.AppliedDate.ShouldBeNull();
        partial.Stage.ShouldBe("Interview");

        await ApplyAsync(new ApplicationSubmitted(
            Guid.CreateVersion7(), id, owner, campaignId, null, new DateOnly(2026, 3, 1), "Referral", "Remote", T1));

        var healed = await FactsAsync(id);
        healed.CampaignId.ShouldBe(campaignId);
        healed.AppliedDate.ShouldBe(new DateOnly(2026, 3, 1));
        healed.Source.ShouldBe("Referral");

        // The submission carries the opening stage, which is older news than the
        // transition already recorded.
        healed.Stage.ShouldBe("Interview");
        healed.StageEnteredAt.ShouldBe(T2);
    }

    [Fact]
    public async Task An_unrecognised_stage_is_stored_without_claiming_a_funnel_step()
    {
        // Nothing publishes this today. The point is that if the pipeline ever
        // grows a stage this module has not heard of, delivery does not start
        // throwing - a projection that throws parks its outbox row and stops the
        // fact reaching anyone.
        var (id, owner) = NewApplication();

        await ApplyAsync(new ApplicationStageChanged(
            Guid.CreateVersion7(), id, owner, "Applied", "Shortlisted", T1));

        var facts = await FactsAsync(id);
        facts.Stage.ShouldBe("Shortlisted");
        facts.ReachedScreeningAt.ShouldBeNull();
        facts.ReachedInterviewAt.ShouldBeNull();
        facts.ReachedOfferAt.ShouldBeNull();
        facts.FirstResponseAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_earliest_booking_is_the_one_kept()
    {
        var (id, owner) = NewApplication();
        var interviewId = Guid.CreateVersion7();

        // A round booked, then moved later - the event is republished with the
        // same interview id each time.
        await ApplyAsync(new InterviewScheduled(Guid.CreateVersion7(), id, interviewId, owner, T2, T1));
        await ApplyAsync(new InterviewScheduled(Guid.CreateVersion7(), id, interviewId, owner, T3, T2));

        (await FactsAsync(id)).FirstInterviewScheduledAt.ShouldBe(T2);
    }

    // ----------------------------------------------------------------- helpers

    private static (Guid Id, UserId Owner) NewApplication() => (Guid.CreateVersion7(), UserId.New());

    private static async Task ShouldSucceedAsync(Task<HttpResponseMessage> request) =>
        (await request).IsSuccessStatusCode.ShouldBeTrue();

    /// <summary>
    /// Hands one event to the projection that handles it, against the real store.
    /// Constructed rather than resolved by interface: a second module registering a
    /// handler for the same event would otherwise silently change what these
    /// assert. The HTTP tests above are what prove the registrations.
    /// </summary>
    private async Task ApplyAsync(IIntegrationEvent integrationEvent)
    {
        using var scope = fixture.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ApplicationFactsWriter>();

        Task handled = integrationEvent switch
        {
            ApplicationSubmitted e => new ApplicationSubmittedProjection(writer).HandleAsync(e, Ct),
            ApplicationStageChanged e => new ApplicationStageChangedProjection(writer).HandleAsync(e, Ct),
            ApplicationReachedTerminal e => new ApplicationReachedTerminalProjection(writer).HandleAsync(e, Ct),
            ApplicationReopened e => new ApplicationReopenedProjection(writer).HandleAsync(e, Ct),
            ApplicationMovedToCampaign e => new ApplicationMovedToCampaignProjection(writer).HandleAsync(e, Ct),
            InterviewScheduled e => new InterviewScheduledProjection(writer).HandleAsync(e, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(integrationEvent)),
        };

        await handled;
    }

    private async Task<ApplicationFacts> FactsAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        return await db.ApplicationFacts
            .AsNoTracking()
            .SingleAsync(f => f.ApplicationId == applicationId, Ct);
    }

    private async Task<ApplicationFacts> WaitForFactsAsync(
        Guid applicationId, Func<ApplicationFacts, bool> until)
    {
        await Poll.UntilAsync(
            async () =>
            {
                using var scope = fixture.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
                var facts = await db.ApplicationFacts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(f => f.ApplicationId == applicationId, Ct);

                return facts is not null && until(facts);
            },
            $"the read model should catch up with application {applicationId}",
            Ct);

        return await FactsAsync(applicationId);
    }
}
