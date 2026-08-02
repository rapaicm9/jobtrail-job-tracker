using System.Net;
using System.Text.Json;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.Modules.Applications.Contracts;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// What a change of campaign announces, read off the real outbox after a real
/// request.
/// <para>
/// Nothing consumes this event yet, which is the point of recording it now: a read
/// model rebuilt from its event stream can only be as complete as the stream, and a
/// move that was never announced is a fact no later consumer can recover. The
/// claims are that both ways an application can change campaign are announced,
/// that a replace which keeps the campaign is not, and that both ends of the move
/// travel so the event can be applied without the ones before it.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CampaignMoveEventTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task A_move_announces_both_ends_of_it()
    {
        var tokens = await ProUserAsync();
        var from = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);
        var to = await CreateCampaignAsync(tokens, "Somewhere else");
        var created = await CreateApplicationAsync(tokens, new { role = "Engineer" });

        await MoveAsync(tokens, created, to.Id);

        var message = await fixture.SingleMessageForAsync(
            created.Id, ApplicationMovedToCampaign.EventType, Ct);

        var payload = JsonDocument.Parse(message.Payload).RootElement;
        payload.GetProperty("FromCampaignId").GetGuid().ShouldBe(from);
        payload.GetProperty("ToCampaignId").GetGuid().ShouldBe(to.Id);
        payload.GetProperty("ApplicationId").GetGuid().ShouldBe(created.Id);
        message.Payload.ShouldContain(tokens.UserId.ToString());

        // The campaign is a grouping, not a note - none of the user's own words
        // belong in an event about it.
        message.Payload.ShouldNotContain("Engineer");
    }

    [Fact]
    public async Task Opening_an_application_announces_no_move()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "Opened here");

        var created = await CreateApplicationAsync(
            tokens, new { role = "Engineer", campaignId = campaign.Id });

        // Where an application started is carried by its submission. A move is a
        // change of campaign, and opening one is not a change.
        (await fixture.EventTypesForAsync(created.Id, Ct))
            .ShouldNotContain(ApplicationMovedToCampaign.EventType);
    }

    [Fact]
    public async Task A_replace_that_keeps_the_campaign_announces_nothing()
    {
        var tokens = await ProUserAsync();
        var created = await CreateApplicationAsync(tokens, new { role = "Engineer" });

        // A full replace invites a client to send back the record it already has.
        // Re-announcing that would have Analytics re-attributing the same
        // application on every edit.
        await MoveAsync(tokens, created, created.CampaignId);

        (await fixture.EventTypesForAsync(created.Id, Ct))
            .ShouldNotContain(ApplicationMovedToCampaign.EventType);
    }

    [Fact]
    public async Task A_refused_move_announces_nothing()
    {
        var mine = await ProUserAsync();
        var theirs = await ProUserAsync();
        var theirCampaign = await CreateCampaignAsync(theirs, "Not yours");
        var created = await CreateApplicationAsync(mine, new { role = "Engineer" });

        var response = await _client.UpdateApplicationAsync(mine.AccessToken, created.Id, new
        {
            role = "Engineer",
            campaignId = theirCampaign.Id,
            appliedDate = created.AppliedDate.ToString("O"),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await fixture.EventTypesForAsync(created.Id, Ct))
            .ShouldNotContain(ApplicationMovedToCampaign.EventType);
    }

    [Fact]
    public async Task Deleting_a_campaign_announces_every_application_it_swept()
    {
        var tokens = await ProUserAsync();
        var defaultId = await fixture.DefaultCampaignIdAsync(UserId.From(tokens.UserId), Ct);
        var doomed = await CreateCampaignAsync(tokens, "About to go");

        var first = await CreateApplicationAsync(tokens, new { role = "First", campaignId = doomed.Id });
        var second = await CreateApplicationAsync(tokens, new { role = "Second", campaignId = doomed.Id });

        (await _client.DeleteCampaignAsync(tokens.AccessToken, doomed.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // One event per application, not one for the campaign: a consumer tracks
        // applications, and a campaign that no longer exists is nothing it can
        // resolve later.
        foreach (var applicationId in new[] { first.Id, second.Id })
        {
            var message = await fixture.SingleMessageForAsync(
                applicationId, ApplicationMovedToCampaign.EventType, Ct);

            var payload = JsonDocument.Parse(message.Payload).RootElement;
            payload.GetProperty("FromCampaignId").GetGuid().ShouldBe(doomed.Id);
            payload.GetProperty("ToCampaignId").GetGuid().ShouldBe(defaultId);
        }
    }

    [Fact]
    public async Task Deleting_an_empty_campaign_announces_nothing()
    {
        var tokens = await ProUserAsync();
        var campaign = await CreateCampaignAsync(tokens, "Never used");
        var untouched = await CreateApplicationAsync(tokens, new { role = "Engineer" });

        (await _client.DeleteCampaignAsync(tokens.AccessToken, campaign.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fixture.EventTypesForAsync(untouched.Id, Ct))
            .ShouldNotContain(ApplicationMovedToCampaign.EventType);
    }

    private Task<AuthTokens> ProUserAsync() => fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);

    private async Task<CampaignView> CreateCampaignAsync(AuthTokens tokens, string name) =>
        await (await _client.CreateCampaignAsync(tokens.AccessToken, new { name })).ReadCampaignAsync();

    private async Task<ApplicationView> CreateApplicationAsync(AuthTokens tokens, object body) =>
        await (await _client.CreateApplicationAsync(tokens.AccessToken, body)).ReadApplicationAsync();

    private async Task MoveAsync(AuthTokens tokens, ApplicationView application, Guid campaignId)
    {
        var response = await _client.UpdateApplicationAsync(tokens.AccessToken, application.Id, new
        {
            role = application.Role,
            campaignId,
            appliedDate = application.AppliedDate.ToString("O"),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
