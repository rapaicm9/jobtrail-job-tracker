using JobTrail.Infrastructure.Events;
using JobTrail.Infrastructure.Persistence;
using JobTrail.Modules.Billing.Authorization;
using JobTrail.Modules.Billing.Contracts;
using JobTrail.Modules.Billing.Features.EraseData;
using JobTrail.Modules.Billing.Features.GetPlan;
using JobTrail.Modules.Billing.Features.GrantPro;
using JobTrail.Modules.Billing.Features.ProvisionPlan;
using JobTrail.Modules.Billing.Features.PurchasePro;
using JobTrail.Modules.Billing.Persistence;
using JobTrail.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace JobTrail.Modules.Billing;

/// <summary>
/// The Billing module's composition surface. A host calls
/// <see cref="AddBillingModule"/> to register the entitlement store and its event
/// reactions, then <see cref="MapBillingEndpoints"/> to expose the plan and
/// purchase slices; everything the module owns stays internal behind it.
/// </summary>
public static class BillingModule
{
    public static IHostApplicationBuilder AddBillingModule(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("jobtrail")
            ?? throw new InvalidOperationException(
                "Connection string 'jobtrail' is not configured. It is injected by the AppHost.");

        builder.Services.AddDbContext<BillingDbContext>(options =>
            NpgsqlContextConfiguration.Configure(options, connectionString, BillingDbContext.Schema));

        // Aspire adds health checks, a retrying execution strategy and telemetry
        // to the context registered above, without owning its configuration.
        builder.EnrichNpgsqlDbContext<BillingDbContext>();

        // Every new account gets its Free plan, off Identity's UserRegistered;
        // an erasure request takes the plan and its purchases back out again.
        builder.Services.AddEventHandler<UserRegistered, PlanProvisioningHandler>();
        builder.Services.AddEventHandler<UserDataDeletionRequested, BillingDataErasureHandler>();

        // The entitlement seam other modules gate on, and the purchase flow that
        // moves a plan onto Pro behind the mocked payment provider.
        builder.Services.AddScoped<IEntitlementQuery, EfEntitlementQuery>();
        builder.Services.AddSingleton<IBillingProvider, MockBillingProvider>();
        builder.Services.AddScoped<GetPlanHandler>();
        builder.Services.AddScoped<PurchaseProHandler>();
        builder.Services.AddScoped<GrantProHandler>();

        // The clock, if no host or module registered it first.
        builder.Services.TryAddSingleton(TimeProvider.System);

        return builder;
    }

    /// <summary>
    /// Registers a <c>Feature:*</c> authorization policy for every
    /// <see cref="Entitlement"/>, each satisfied only when the entitlement query
    /// says the caller holds it. Any module gates an endpoint by policy name
    /// through <see cref="FeaturePolicy"/> - it never references Billing to do so,
    /// and the client can never assert its own entitlement.
    /// </summary>
    public static IServiceCollection AddBillingFeaturePolicies(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, FeatureAuthorizationHandler>();

        services.Configure<AuthorizationOptions>(options =>
        {
            foreach (var entitlement in Enum.GetValues<Entitlement>())
            {
                options.AddPolicy(FeaturePolicy.For(entitlement), policy =>
                {
                    // An unauthenticated caller is a 401, not a 403 - the check is
                    // "which user", then "may that user".
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new FeatureRequirement(entitlement));
                });
            }
        });

        return services;
    }

    /// <summary>
    /// Maps Billing's authenticated slices onto the host's versioned API group,
    /// under <c>/billing</c>: read the caller's plan status, and unlock Pro through
    /// the mocked provider. A sibling of the account group - these take the host's
    /// general per-IP budget. Returns the group so the host can layer its own
    /// policy. Developer-only shortcuts stay in <see cref="MapBillingDevEndpoints"/>.
    /// </summary>
    public static RouteGroupBuilder MapBillingEndpoints(this IEndpointRouteBuilder api)
    {
        var billing = api.MapGroup("/billing");

        GetPlanEndpoint.Map(billing);
        PurchaseProEndpoint.Map(billing);

        return billing;
    }

    /// <summary>
    /// Maps Billing's developer-only endpoints. The host calls this only in
    /// Development, so the shortcuts it exposes - granting Pro without a purchase -
    /// can never exist in production. Returns the group so the host can layer its
    /// own policy.
    /// </summary>
    public static RouteGroupBuilder MapBillingDevEndpoints(this IEndpointRouteBuilder api)
    {
        var billingDev = api.MapGroup("/billing/dev");

        GrantProEndpoint.Map(billingDev);

        return billingDev;
    }
}
