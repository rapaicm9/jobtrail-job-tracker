using System.Net;
using Jobspect.IntegrationTests.Infrastructure;
using Jobspect.SharedKernel;
using Shouldly;

namespace Jobspect.IntegrationTests;

/// <summary>
/// The <c>Feature:*</c> entitlement gate, end to end over the whole pipeline and
/// the real store: bearer token → <c>IUserContext</c> → the named policy →
/// <c>FeatureAuthorizationHandler</c> → <c>IEntitlementQuery</c> → the plan row.
/// <para>
/// Sprint 5 proved that chain's decision at the authorization-handler level and
/// left the edge unproven, because nothing shipped a gated route to hit. Custom
/// fields are the first, so this is where that closes: the endpoint is the subject,
/// not the handler.
/// </para>
/// <para>
/// The split between what is gated and what is not is the other claim here.
/// Defining a field is the paid capability; reading definitions back is not, and
/// must not be - an account without the entitlement still has to be able to make
/// sense of the values it recorded while it had one.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomFieldGateTests(ApiFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object Definition = new { label = "Referral source", type = "text" };

    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task Pro_may_define_a_field()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);

        (await _client.CreateCustomFieldAsync(tokens.AccessToken, Definition))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Free_is_forbidden_from_defining_a_field()
    {
        var tokens = await _client.RegisterNewUserAsync();

        // Registration provisions the Free plan asynchronously. Waiting for it means
        // the 403 is the entitlement being absent, not the plan not existing yet -
        // otherwise this would pass for the wrong reason.
        await Poll.UntilAsync(
            async () => await fixture.PlanForAsync(UserId.From(tokens.UserId), Ct) is not null,
            "registration should provision the Free plan the gate reads",
            Ct);

        (await _client.CreateCustomFieldAsync(tokens.AccessToken, Definition))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_anonymous_caller_is_challenged_rather_than_forbidden()
    {
        // 401, not 403: the question is "which user" before "may that user", so a
        // caller who has not said who they are is asked, not refused.
        (await _client.CreateCustomFieldAsync(accessToken: null, Definition))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await _client.UpdateCustomFieldAsync(accessToken: null, Guid.CreateVersion7(), Definition))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Free_is_forbidden_from_editing_a_field()
    {
        var pro = await fixture.RegisterProUserAsync(_client, Ct);
        var created = await (await _client.CreateCustomFieldAsync(pro.AccessToken, Definition))
            .ReadCustomFieldAsync();

        var free = await _client.RegisterNewUserAsync();

        // Forbidden before ownership is ever considered - the gate runs at
        // authorization, so this is a 403 rather than the 404 another user's field
        // would otherwise be.
        (await _client.UpdateCustomFieldAsync(
                free.AccessToken, created.Id, new { label = "Mine now", isArchived = false }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Free_may_still_read_the_definitions_it_holds()
    {
        var tokens = await fixture.RegisterProUserAsync(_client, Ct);
        var created = await (await _client.CreateCustomFieldAsync(tokens.AccessToken, Definition))
            .ReadCustomFieldAsync();

        // The same account without the entitlement is what a downgrade looks like.
        // Its values are retained read-only, which is only meaningful if it can
        // still fetch the definitions that say what they mean.
        await fixture.SetTierToFreeAsync(UserId.From(tokens.UserId), Ct);

        (await _client.GetCustomFieldAsync(tokens.AccessToken, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = await (await _client.ListCustomFieldsAsync(tokens.AccessToken)).ReadCustomFieldListAsync();
        listed.ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        // And is refused the moment it tries to define another.
        (await _client.CreateCustomFieldAsync(tokens.AccessToken, new { label = "Another", type = "text" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_still_demands_authentication()
    {
        (await _client.ListCustomFieldsAsync(accessToken: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.GetCustomFieldAsync(accessToken: null, Guid.CreateVersion7()))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
