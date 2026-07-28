using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Applications.Features.CreateCampaign;
using JobTrail.Modules.Applications.Features.CreateCustomField;
using JobTrail.Modules.Applications.Features.UpdateCustomField;
using JobTrail.Modules.Identity.Features.ExportAccount;
using JobTrail.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// The second entitlement check, the one behind the route policy.
/// <para>
/// These handlers sit behind <c>Feature:*</c> policies, so an unentitled caller is
/// refused before the handler is reached and this path is unreachable over HTTP.
/// That is exactly why it is worth a test: an unreachable branch is an untested
/// branch, and the reason it exists is that the route may not always be the way in
/// - a policy dropped from a registration, or the handler called from a worker or
/// another slice, and the gate has to hold on its own.
/// </para>
/// <para>
/// So the handlers are resolved from a real request scope and called directly,
/// against the real entitlement store with the account left on Free. No fakes: what
/// is under test is whether the handler asks at all.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EntitlementDefenceInDepthTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Defining_a_custom_field_refuses_a_free_account()
    {
        var ownerId = await FreeAccountAsync();

        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateCustomFieldHandler>();

        var result = await handler.HandleAsync(
            ownerId, new CreateCustomFieldRequest("Referral source", "text", null), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Code.ShouldBe("custom_field.definitions_not_entitled");
    }

    [Fact]
    public async Task Editing_a_custom_field_refuses_a_free_account()
    {
        var ownerId = await FreeAccountAsync();

        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<UpdateCustomFieldHandler>();

        // Refused before the field is looked up, so the answer is "you may not do
        // this" rather than a 404 about a field the caller was never allowed to edit.
        var result = await handler.HandleAsync(
            ownerId, Guid.CreateVersion7(), new UpdateCustomFieldRequest("Renamed", null, false), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("custom_field.definitions_not_entitled");
    }

    [Fact]
    public async Task Opening_another_campaign_refuses_a_free_account()
    {
        var ownerId = await FreeAccountAsync();

        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateCampaignHandler>();

        var result = await handler.HandleAsync(ownerId, new CreateCampaignRequest("A second search"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Code.ShouldBe("campaign.not_entitled");
    }

    [Fact]
    public async Task Exporting_refuses_a_free_account()
    {
        var ownerId = await FreeAccountAsync();

        using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ExportAccountHandler>();

        var result = await handler.HandleAsync(ownerId, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("account.export_not_entitled");
    }

    [Fact]
    public async Task And_all_four_go_through_for_a_pro_account()
    {
        // The other half of the claim: the guard refuses the unentitled without
        // standing in the way of everyone else. Without this, every test above
        // would still pass if the handlers simply always refused.
        var tokens = await fixture.RegisterProWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        using var scope = fixture.CreateScope();

        var field = await scope.ServiceProvider.GetRequiredService<CreateCustomFieldHandler>()
            .HandleAsync(ownerId, new CreateCustomFieldRequest("Referral source", "text", null), Ct);
        field.IsSuccess.ShouldBeTrue();

        var renamed = await scope.ServiceProvider.GetRequiredService<UpdateCustomFieldHandler>()
            .HandleAsync(ownerId, field.Value.Id, new UpdateCustomFieldRequest("How I found it", null, false), Ct);
        renamed.IsSuccess.ShouldBeTrue();

        var campaign = await scope.ServiceProvider.GetRequiredService<CreateCampaignHandler>()
            .HandleAsync(ownerId, new CreateCampaignRequest("2026 backend roles"), Ct);
        campaign.IsSuccess.ShouldBeTrue();

        var export = await scope.ServiceProvider.GetRequiredService<ExportAccountHandler>()
            .HandleAsync(ownerId, Ct);
        export.IsSuccess.ShouldBeTrue();
        export.Value.ShouldNotBeEmpty();
    }

    /// <summary>
    /// A registered account whose plan has landed and stayed Free - which is what
    /// every handler here is being asked to refuse.
    /// </summary>
    private async Task<UserId> FreeAccountAsync()
    {
        var tokens = await fixture.RegisterWithDefaultCampaignAsync(_client, Ct);
        var ownerId = UserId.From(tokens.UserId);

        // The plan is provisioned asynchronously off registration. Without waiting,
        // a refusal could be the plan not existing yet rather than the entitlement
        // being absent - the same answer for the wrong reason.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(ownerId, Ct) is not null,
            "registration should provision the Free plan the handlers read",
            Ct);

        return ownerId;
    }
}
