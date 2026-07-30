using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Analytics.Features.EraseData;
using JobTrail.Modules.Analytics.Persistence;
using JobTrail.Modules.Applications;
using JobTrail.Modules.Identity.Contracts;
using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Analytics' share of the erasure fan-out. One table, so one delete - but it
/// ships with the projections rather than after them, because the moment the read
/// model starts being filled, an account deletion that leaves it behind is an
/// erasure that reports success while data remains.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsErasureTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task An_account_deletion_takes_the_read_model_with_it()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        var created = await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        // Wait for the projection, or the erasure below would prove nothing.
        await Poll.UntilAsync(
            async () => await RowCountAsync(ownerId) > 0,
            "the read model should hold the application before the account is erased",
            Ct);

        (await _client.DeleteAccountAsync(tokens.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await Poll.UntilAsync(
            async () => await RowCountAsync(ownerId) == 0,
            "deleting the account should erase the analytics rows behind it",
            Ct);

        (await FactsExistAsync(created.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task It_leaves_another_users_rows_alone()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();

        await EraseAsync(mine);

        (await RowCountAsync(mine)).ShouldBe(0);
        (await RowCountAsync(theirs)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Erasing_twice_is_not_an_error()
    {
        // At-least-once delivery means the handler can be asked to do this again.
        var ownerId = await SeedAsync();

        await EraseAsync(ownerId);
        await Should.NotThrowAsync(EraseAsync(ownerId));

        (await RowCountAsync(ownerId)).ShouldBe(0);
    }

    [Fact]
    public async Task Erasing_a_user_who_has_nothing_here_is_not_an_error() =>
        await Should.NotThrowAsync(EraseAsync(UserId.New()));

    /// <summary>
    /// The ordering the erasure fan-out depends on: this module's handler has to
    /// run after the Applications module's, which deletes the events still owed on
    /// the user's behalf. Reversed, an owed event delivered in the gap would
    /// rebuild a row for an account that had just been erased.
    /// <para>
    /// Handlers run in registration order, so the guarantee is really about where
    /// the host calls <c>AddAnalyticsModule</c> - which is invisible at the call
    /// site and exactly the sort of thing a tidy-up reorders. Asserted here rather
    /// than left to a comment.
    /// </para>
    /// </summary>
    [Fact]
    public void Its_erasure_runs_after_the_module_whose_events_it_consumes()
    {
        using var scope = fixture.CreateScope();

        var handlers = scope.ServiceProvider
            .GetServices<JobTrail.SharedKernel.Events.IEventHandler<UserDataDeletionRequested>>()
            .Select(handler => handler.GetType())
            .ToList();

        var applications = handlers.FindIndex(type =>
            type.Assembly == typeof(ApplicationsModule).Assembly);
        var analytics = handlers.FindIndex(type => type == typeof(AnalyticsDataErasureHandler));

        applications.ShouldBeGreaterThanOrEqualTo(0, "the Applications module should erase its own data");
        analytics.ShouldBeGreaterThan(
            applications,
            "Analytics must erase after Applications has removed the events still owed for the user");
    }

    private async Task<UserId> SeedAsync()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        await (await _client.CreateApplicationAsync(
            tokens.AccessToken, new { role = "Engineer" })).ReadApplicationAsync();

        await Poll.UntilAsync(
            async () => await RowCountAsync(ownerId) > 0,
            "the seeded application should reach the read model",
            Ct);

        return ownerId;
    }

    private async Task EraseAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        await new AnalyticsDataErasureHandler(dbContext)
            .HandleAsync(new UserDataDeletionRequested(Guid.CreateVersion7(), ownerId), Ct);
    }

    private async Task<int> RowCountAsync(UserId ownerId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        return await db.ApplicationFacts.CountAsync(f => f.OwnerId == ownerId, Ct);
    }

    private async Task<bool> FactsExistAsync(Guid applicationId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        return await db.ApplicationFacts.AnyAsync(f => f.ApplicationId == applicationId, Ct);
    }
}
