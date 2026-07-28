using JobTrail.IntegrationTests.Infrastructure;
using JobTrail.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobTrail.IntegrationTests;

/// <summary>
/// Exactly which routes the real host gates behind a <c>Feature:*</c> policy,
/// asserted as a set.
/// <para>
/// This is the guard for the failure that can actually happen. A gate is one call
/// on one route registration, and the custom-field endpoints already had theirs
/// moved off their group and onto individual routes so that reading could stay
/// open - the kind of edit that silently drops a policy from one endpoint while
/// looking entirely correct. Nothing else notices: the endpoint keeps working, for
/// everybody.
/// </para>
/// <para>
/// It fails in both useful directions. Drop a policy and its entry goes missing;
/// add a gated endpoint and an unexpected one appears - which is the moment to
/// give its handler the matching re-check (see
/// <see cref="EntitlementDefenceInDepthTests"/>).
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FeatureGatedEndpointTests(ApiFixture fixture)
{
    /// <summary>
    /// Every gated route, spelled out rather than derived. A test that reads the
    /// gates off the same registrations it is checking would agree with itself no
    /// matter what those registrations said.
    /// </summary>
    /// <remarks>
    /// The trailing slash on the two group-root routes is not a typo: an endpoint
    /// mapped with an empty pattern on a group renders as "<c>{group}/</c>" here,
    /// though it is reached without one.
    /// </remarks>
    private static readonly string[] Expected =
    [
        $"GET /api/v{{version:apiVersion}}/account/export -> {FeaturePolicy.For(Entitlement.Export)}",
        $"POST /api/v{{version:apiVersion}}/campaigns/ -> {FeaturePolicy.For(Entitlement.MultipleCampaigns)}",
        $"POST /api/v{{version:apiVersion}}/custom-fields/ -> {FeaturePolicy.For(Entitlement.CustomFields)}",
        $"PUT /api/v{{version:apiVersion}}/custom-fields/{{id:guid}} -> {FeaturePolicy.For(Entitlement.CustomFields)}",
    ];

    [Fact]
    public void The_gated_routes_are_exactly_these()
    {
        GatedRoutes().ShouldBe(Expected, ignoreOrder: true);
    }

    [Fact]
    public void Every_gate_names_a_policy_that_is_actually_registered()
    {
        // A policy name is a string, so a typo would gate an endpoint on a policy
        // that does not exist - which ASP.NET reports at request time, not startup.
        var registered = Enum.GetValues<Entitlement>().Select(FeaturePolicy.For).ToHashSet(StringComparer.Ordinal);

        foreach (var route in GatedRoutes())
        {
            registered.ShouldContain(route.Split(" -> ")[1]);
        }
    }

    /// <summary>
    /// Every route in the running host that carries a <c>Feature:*</c> authorization
    /// policy, as "METHOD pattern -> policy".
    /// </summary>
    private IEnumerable<string> GatedRoutes()
    {
        using var scope = fixture.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints;

        return [.. endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => endpoint.Metadata
                .OfType<IAuthorizeData>()
                .Where(authorize => authorize.Policy?.StartsWith(FeaturePolicy.Prefix, StringComparison.Ordinal) == true)
                .Select(authorize => $"{MethodOf(endpoint)} {endpoint.RoutePattern.RawText} -> {authorize.Policy}"))];
    }

    private static string MethodOf(RouteEndpoint endpoint) =>
        string.Join('|', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []);
}
