using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Domain;
using JobTrail.Modules.Applications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/applications/{id}/transition</c> against a real database: the
/// aggregate's state machine decides every move, so legal ones apply and record a
/// timeline entry while illegal ones are a 422 that leaves the application where
/// it was. An unknown stage is a validation 422 distinct from an illegal move, and
/// another user's application is a 404.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransitionApplicationEndpointTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Advances_the_application_and_stamps_it()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);
        application.UpdatedAt.ShouldBeNull();

        var moved = await (await _client.TransitionApplicationAsync(
            tokens.AccessToken, application.Id, "Screening")).ReadApplicationAsync();

        moved.Stage.ShouldBe("Screening");
        moved.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Jumps_straight_from_Applied_to_a_terminal()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);

        var moved = await (await _client.TransitionApplicationAsync(
            tokens.AccessToken, application.Id, "Rejected")).ReadApplicationAsync();

        moved.Stage.ShouldBe("Rejected");
    }

    [Fact]
    public async Task Reopens_a_terminal_application()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);
        await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, "Ghosted");

        var reopened = await (await _client.TransitionApplicationAsync(
            tokens.AccessToken, application.Id, "Screening")).ReadApplicationAsync();

        reopened.Stage.ShouldBe("Screening");
    }

    [Fact]
    public async Task Reclassifies_one_terminal_as_another()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);
        await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, "Ghosted");

        // A ghost whose rejection finally arrives.
        var reclassified = await (await _client.TransitionApplicationAsync(
            tokens.AccessToken, application.Id, "Rejected")).ReadApplicationAsync();

        reclassified.Stage.ShouldBe("Rejected");
    }

    [Fact]
    public async Task Rejects_an_illegal_move_and_leaves_the_stage_unchanged()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);

        // Accepted is reachable from Offer only, never straight from Applied.
        var response = await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, "Accepted");

        await response.ShouldBeProblemAsync(422, "application.illegal_transition");
        var unchanged = await (await _client.GetApplicationAsync(tokens.AccessToken, application.Id)).ReadApplicationAsync();
        unchanged.Stage.ShouldBe("Applied");
    }

    [Fact]
    public async Task Rejects_an_unknown_stage_as_a_validation_error()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);

        var response = await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, "Banana");

        await response.ShouldBeValidationProblemAsync("targetStage");
    }

    [Fact]
    public async Task Returns_404_for_another_users_application()
    {
        var owner = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var other = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(owner.AccessToken);

        var response = await _client.TransitionApplicationAsync(other.AccessToken, application.Id, "Screening");

        await response.ShouldBeProblemAsync(404, "application.not_found");
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.TransitionApplicationAsync(accessToken: null, Guid.NewGuid(), "Screening");

        ((int)response.StatusCode).ShouldBe(401);
    }

    [Fact]
    public async Task Records_a_stage_change_entry_on_the_timeline()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var application = await CreateApplicationAsync(tokens.AccessToken);

        await _client.TransitionApplicationAsync(tokens.AccessToken, application.Id, "Screening");

        var entries = await ActivityForAsync(application.Id);

        // The creation entry, then the move - both ends and the kind captured.
        entries.Select(e => e.Kind).ShouldBe([ActivityKind.Created, ActivityKind.StageChanged]);
        var move = entries[1];
        move.FromStage.ShouldBe(Stage.Applied);
        move.ToStage.ShouldBe(Stage.Screening);
        move.TransitionKind.ShouldBe(TransitionKind.Advance);
    }

    private async Task<ApplicationView> CreateApplicationAsync(string? accessToken) =>
        await (await _client.CreateApplicationAsync(accessToken, new { role = "Backend Engineer" })).ReadApplicationAsync();

    private async Task<IReadOnlyList<ActivityLogEntry>> ActivityForAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        return await db.ActivityLog
            .Where(e => e.ApplicationId == applicationId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(Ct);
    }
}
